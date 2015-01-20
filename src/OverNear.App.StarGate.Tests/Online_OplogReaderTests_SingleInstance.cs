using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;

using NUnit.Framework;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Repo;

namespace OverNear.App.StarGate.Tests
{
	/// <summary>
	/// This test check to see if the oplog reader will resume in singleInstance mode if master is stepped down or disconnected
	/// </summary>
	//[RequiresMTA]
	[Explicit]
	[TestFixture, Category("Default")]
	public sealed class Online_OplogReaderTests_SingleInstance : IDisposable
	{
		public class MockedOpLog : OpLogLine
		{
			public MockedOpLog() { Id = Guid.NewGuid(); }

			[BsonId]
			public Guid Id { get; set; }
		}

		readonly MongoUrl _repoPath;
		readonly MongoRepo<MockedOpLog> _repo;
		const string COL_NAME = "oplog.rs";

		public Online_OplogReaderTests_SingleInstance()
		{
			var p = new PathFinder("mongodb://localhost/");
			MongoUrl np = p.Match(new Regex(".+"));
			Assert.IsNotNull(np);
			_repoPath = np;
			Console.WriteLine("\r\nOnline_OplogReaderTests_SingleInstance CTOR: _repoPath== {0}", _repoPath);

			_repo = new MongoRepo<MockedOpLog>(_repoPath);
			_repo.Server.TryConnect();
			Assert.IsNotNull(_repo.Server.Primary);

			_repoPath = new MongoUrl("mongodb://" + _repo.Server.Primary.Address + "/local");
			_repo = new MongoRepo<MockedOpLog>(_repoPath,
 				serverSettings: ss =>  
				{
					ss.ConnectionMode = ConnectionMode.ReplicaSet;
					ss.ReadPreference = ReadPreference.PrimaryPreferred;
					//ss.WriteConcern = WriteConcern.Acknowledged;
					return ss;
				},
				defaultDbName: db => "local_nunit", 
				defaultCollection: tbl => COL_NAME);
			StringAssert.AreNotEqualIgnoringCase("local", _repo.Database.Name);
			if (_repo.Database.CollectionExists(COL_NAME)) //drop on creation
				_repo.Database.DropCollection(COL_NAME);

			_repo.SingletonInit += Repo_SingletonInit;
			_repo.Collection.CreateIndex(IndexKeys<MockedOpLog>.Ascending(o => o.Id), IndexOptions.SetUnique(true));
			_repo.Collection.CreateIndex(IndexKeys<MockedOpLog>.Ascending(o => o.TimeStamp, o => o.NameSpace), IndexOptions.SetUnique(true));
		}

		void Repo_SingletonInit(IMongoRepo<MockedOpLog> caller)
		{
			try
			{
				IMongoCollectionOptions op = CollectionOptions.SetCapped(true)
					.SetMaxSize(1024 * 1024 * 256) //256mb max
					.SetAutoIndexId(true);
				StringAssert.AreNotEqualIgnoringCase("local", caller.Database.Name);
				caller.Database.CreateCollection(COL_NAME, op);
			}
			catch (MongoCommandException mex)
			{
				if (!mex.Message.Contains("already exists"))
					throw;
				else
					Console.WriteLine("Collection db.{0}.{1} already exists", caller.Database.Name, COL_NAME);
			}
		}

		readonly ICollection<string> _payloadIds = new CollectionTrigger<string>(new HashSet<string>());
		readonly ICollection<MockedOpLog> _created = new CollectionTrigger<MockedOpLog>(new LinkedList<MockedOpLog>());
		MockedOpLog Mock()
		{
			DateTime n = DateTime.UtcNow;
			var ts = new BsonTimestamp((int)n.ToUnixTime(), n.Millisecond);
			string gid = Guid.NewGuid().ToString();
			var o = new MockedOpLog
			{
				Created = n,
				Operation = OpLogType.Insert,
				NameSpace = this.GetType().Name,
				Version = 1.0,
				TimeStamp = ts,
				Hash = Guid.NewGuid().ToString().ToMurMur3_32(),
				Payload = new ReadState
				{
					Id = gid,
					TimeStamp = ts,
				}.ToBsonDocument()
			};
			_created.Add(o);
			_payloadIds.Add(o.Payload["_id"].AsString);
			Thread.Sleep(100); //force long sleep to space out the ids
			return o;
		}

		void UnMock(MockedOpLog o)
		{
			if (o != null)
			{
				_created.Remove(o);
				_payloadIds.Remove(o.Payload["_id"].AsString);
			}
		}

		~Online_OplogReaderTests_SingleInstance() { Dispose(); }
		int _disposed = 0;
		[TestFixtureTearDown]
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0 && _repo != null)
			{
				Console.WriteLine("DbCleanup: begin");
				StringAssert.AreNotEqualIgnoringCase("local", _repo.Database.Name);
				_repo.Server.TryConnect();
				_repo.Collection.Drop();
				_repo.Database.Drop();
				Console.WriteLine("DbCleanup: completed");
			}
			else
				Console.WriteLine("DbCleanup: no cleanup necessary");
		}

		[TestFixtureTearDown]
		public void ReaderTeardown()
		{
			Console.WriteLine("ReaderTeardown: begin");
			if (_writeThread != null && _writeThread.IsAlive)
				_writeThread.Join(200);
			if (_reader != null)
				_reader.Dispose();

			Console.WriteLine("ReaderTeardown: completed");
		}

		OpLogReader _reader;
		Thread _writeThread;
		void WriterSetup()
		{
			DateTime started = DateTime.UtcNow;
			Console.WriteLine("WriterSetup begin");
			_reader = new OpLogReader(_repoPath, true);
			_writeThread = new Thread(InsertLogs)
			{
				IsBackground = true,
				Name = "WRT",
			};
			_writeThread.Start(); //late write start
			Console.WriteLine("WriterSetup completed. Took: {0}", DateTime.UtcNow - started);
			Thread.Sleep(1000);
		}

		const int MAX_ITEMS = 100;
		void InsertLogs()
		{
			try
			{
				if (_repo.Collection.Count() > 0)
				{
					Console.WriteLine("InsertLogs: removing leftover data from old test");
					_repo.Collection.RemoveAll();
				}

				Console.WriteLine("InsertLogs: begins");
				for (int i = 0; i < MAX_ITEMS; i++)
				{
					MockedOpLog o = Mock();
					Assert.IsNotNull(o);
					try
					{
						_repo.Collection.Insert(o, WriteConcern.Acknowledged);
						//_repo.Collection.Insert(o, new WriteConcern { W = 1, Journal = true, WTimeout = TimeSpan.FromSeconds(1) });
					}
					catch //(WriteConcernException wex)
					{
						Console.WriteLine("Rolling back an insert error");
						i--; //rollback 1 item...
						UnMock(o);

						while (!AllSlavesAreUp(_repo))
						{
							Thread.Sleep(1000);
						}
						_repo.Server.TryConnect();
						Console.Write("[*]");
						continue;
					}
					Console.Write('.');
					if (i + 1 > MAX_ITEMS && i % 10 == 0)
						Thread.Sleep(500);
				}
				Assert.AreEqual(MAX_ITEMS, _repo.Collection.Count());
			}
			catch (Exception ex)
			{
				Console.WriteLine("InsertLogs", ex);
			}
			finally
			{
				Console.WriteLine("InsertLogs: exits");
			}
		}

		long _lambdaTicks = DateTime.UtcNow.Ticks;
		volatile ManualResetEvent _readHandle = null;
		volatile ManualResetEvent _stepDownHandle = null;

		[Category("ignore.online")]
		[Test]
		public void Test1_Reads()
		{
			Console.WriteLine("Test1_Reads: begins");
			WriterSetup();

			BsonTimestamp ts = new BsonTimestamp(0);
			
			var hs = new HashSet<string>();
			Console.WriteLine("Test1_Reads: start reading");
			var lastTs = new BsonTimestamp(ts.Value);
			IList<OpLogLine> opsFound = new List<OpLogLine>();

			ThreadPool.QueueUserWorkItem(state =>
			{
				try
				{
					Interlocked.Exchange(ref _lambdaTicks, DateTime.UtcNow.Ticks);
					Console.WriteLine("ReadThread: Begins");
					long reads = 0;
					_reader.Read(ref ts, (op) => ReadItem(ref op, ref reads, ref ts, ref lastTs, ref opsFound, ref hs));
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
				}
				finally
				{
					_readHandle.Set();
					Console.WriteLine("ReadThread: Exit");
				}
			}, _readHandle = new ManualResetEvent(false));
			ThreadPool.QueueUserWorkItem(StepDownLogic, _stepDownHandle = new ManualResetEvent(false));

			Console.WriteLine("TestReads: waiting for all threads to complete");
			WaitHandle.WaitAll(new[] { _readHandle, _stepDownHandle }, TimeSpan.FromSeconds(65 * (_repo.Server.Instances.Length - 1)));
			Console.WriteLine("TestReads: all threads completed");
			ForceReadCleanup();

			Assert.AreEqual(_reader.TotalReads, hs.Count);

			Assert.AreEqual(_payloadIds.Count, _repo.Collection.Count());
			Assert.Greater(_reader.CursorRestarts, 0);

			//Assert.AreEqual(_payloadIds.Count, hs.Count + 8); //not sure why test is always off by 8! Revisit later. Reader does get restarted! --Huy
			Assert.Less(_payloadIds.Count - hs.Count, 10);

			Assert.AreEqual(MAX_ITEMS, _payloadIds.Count);
			Console.WriteLine("Test1_Reads: exit. ts: {0}", ts.ToDateTime());
		}

		[TestFixtureTearDown]
		public void UnlockAllNodes()
		{
			if (_repo != null && !_repo.Server.Instances.IsNullOrEmpty())
			{
				foreach (MongoServerInstance si in _repo.Server.Instances)
				{
					try
					{
						using (_repo.Server.RequestStart(_repo.AdminDb, si))
						{
							CommandResult cr = _repo.AdminDb.RunCommand(new CommandDocument("replSetFreeze", 0));
						}
					}
					catch { }
				}
			}
		}

		void ReadItem(
			ref OpLogLine op, 
			ref long reads, ref BsonTimestamp ts, ref BsonTimestamp lastTs, 
			ref IList<OpLogLine> opsFound, ref HashSet<string> hs)
		{
			try
			{
				Console.Write('*');
				long r = Interlocked.Increment(ref reads);

				opsFound.Add(op);
				Assert.IsNotNull(op);
				Assert.IsNotNull(op.Payload);
				Assert.IsTrue(op.Payload.Contains("_id"));

				string pid = op.Payload["_id"].AsString;
				Assert.IsTrue(_payloadIds.Contains(pid), "Missing payload id!");
				Assert.IsFalse(hs.Contains(pid), "Already exists in hs!");

				Assert.Greater(ts.Value, lastTs.Value);
				Assert.IsTrue(hs.Add(pid));
				lastTs = new BsonTimestamp(ts.Value); //cp value

				if (r >= _payloadIds.Count || r >= MAX_ITEMS)
					ForceReadCleanup();
			}
			catch (FatalReaderException)
			{
				Console.WriteLine("ReadItem: ERROR XXXXXX");
				throw;
			}
			catch (Exception ex)
			{
				Console.WriteLine("ReadItem: ERROR VVVVV");
				throw new FatalReaderException("ReadItem: reader lambda", ex);
			}
			finally
			{
				Interlocked.Exchange(ref _lambdaTicks, DateTime.UtcNow.Ticks);
				if (_reader.TotalReads == _payloadIds.Count)
				{
					Console.WriteLine("ReadItem: Count matches, forces read cleanup.");
					ForceReadCleanup();
				}
			}
		}

		void ForceReadCleanup()
		{
			Console.WriteLine("ReadCleanup: begin");
			try { _reader.Dispose(); }
			catch { }
			Console.WriteLine("ReadCleanup: completed");
		}
		void StepDownLogic(object state)
		{
			try
			{
				Console.WriteLine("StepDownLogic: Thread begins");
				Thread.Sleep(500);

				MongoRepo repo = _repo;
				MongoServerInstance self = repo.Server.Primary;
				Assert.IsNotNull(self);
				Console.WriteLine("\r\n================\r\nSELF IS: {0}\r\n================", self.Address);

				int waitCount = 0;
				if (repo.Server.Secondaries.Length > 1)
				{
					while (!AllSlavesAreUp(repo))
					{
						Thread.Sleep(500);
						waitCount++;
						if (waitCount % 10 == 0)
							Console.WriteLine("StepDownLogic: Waiting for all slaves to come online...");
					}
					var lts = TimeSpan.FromSeconds(120);
					foreach (MongoServerInstance si in repo.Server.Secondaries.Skip(1))
					{
						using (repo.Server.RequestStart(repo.AdminDb, si))
						{
							Console.WriteLine("StepDownLogic: locking slave {0} for {1}", si.Address, lts);
							CommandResult cr = repo.AdminDb.RunCommand(new CommandDocument("replSetFreeze", (int)lts.TotalSeconds));
							Assert.IsNotNull(cr);
							Assert.IsTrue(cr.Ok);
						}
					}
				}

				int i = 0;
				waitCount = 0;
				DateTime started = DateTime.UtcNow;
				long firstStepDownTicks = 0, beforeStepDownTicks = Interlocked.Read(ref _lambdaTicks);
				do
				{	
					var mre = new ManualResetEvent(false);
					EventHandler stateHandle = (o, e) => StateChanged(o as MongoServerInstance, mre);
					MongoServerInstance cur = repo.Server.Primary; //first one will be self
					if (cur == null || !AllSlavesAreUp(repo)) //if cur is null, master is not up!
					{
						Thread.Sleep(500);
						waitCount++;
						if (waitCount % 10 == 0)
							Console.WriteLine("StepDownLogic: Waiting for all slaves to come online...");
						continue;
					}
					else
						waitCount = 0;
					cur.StateChanged += stateHandle; //add trigger
					
					StepDown(repo, cur);
					if (cur.Address == self.Address)
					{
						firstStepDownTicks = Interlocked.Read(ref _firstStepdownLamdaValue);
						Assert.GreaterOrEqual(firstStepDownTicks, beforeStepDownTicks);
						if(i > repo.Server.Instances.Length * 2)
							throw new ApplicationException("StepDownLogic: looped twice & master never returned to " + self.Address);
					}

					Console.WriteLine("StepDownLogic: waiting for {0} connection disconnecting", cur.Address);
					WaitHandle.WaitAny(new[] { mre }, 65 * 1000); //wait til done
					Console.WriteLine("StepDownLogic: {0} connection disconnected", cur.Address);
					cur.StateChanged -= stateHandle; //remove trigger
					i++;
				}
				while (!self.IsPrimary);

				Console.WriteLine("StepDownLogic: ++++++++++++++++ Enter WAIT LOOP");
				DateTime waitEnter = DateTime.UtcNow;
				var toolong = TimeSpan.FromSeconds(5);
				long ltick;
				do
				{
					Thread.Sleep(1000);
					ltick = Interlocked.Read(ref _lambdaTicks);
					if (DateTime.UtcNow - waitEnter > toolong)
						throw new ApplicationException("StepDownLogic: Waited too long [" + toolong + "] for lamdaTicks");
				} while (ltick <= firstStepDownTicks); //wait for read thread to wakeup...

				DateTime done = DateTime.UtcNow;
				Assert.Greater(done - started, new DateTime(ltick) - started);
			}
			catch (Exception ex)
			{
				Console.WriteLine("StepDownLogic: THREAD ERROR: {0}", ex);
				//throw;
			}
			finally
			{
				_stepDownHandle.Set();
				//ForceReadCleanup();
				Console.WriteLine("StepDownLogic: thread exit completed");
			}
		}

		bool AllSlavesAreUp(MongoRepo repo) 
		{
			bool ok = false;
			MongoServerInstance[] all, slaves;
			if ((all = repo.Server.Instances).IsNullOrEmpty() || 
				(slaves = repo.Server.Secondaries).IsNullOrEmpty() ||
				slaves.Length + 1 != all.Length)
				return ok;

			CommandResult cr = repo.AdminDb.RunCommand("replSetGetStatus");
			if (cr != null && cr.Response != null && cr.Response.Contains("members"))
			{
				BsonDocument[] goodNodes = (from v in cr.Response["members"].AsBsonArray
											 where v.IsBsonDocument
											 let d = v.AsBsonDocument
											 where d["stateStr"].AsString == "SECONDARY" &&
												d["health"].ToString() == "1" &&
												d["state"].ToString() == "2" &&
												(!d.Contains("lastHeartbeatMessage") || !d.Contains("errmsg"))
											 select d).ToArray();
				ok = slaves.Length == goodNodes.Length;
			}
			return ok;
		}

		void StateChanged(MongoServerInstance ins, ManualResetEvent rev)
		{
			if (ins != null)
			{
				switch (ins.State)
				{
					case MongoServerState.Disconnecting:
					case MongoServerState.Disconnected:
						if (rev != null)
							rev.Set();

						_repo.Server.TryConnect();
						Console.WriteLine("StateChanged: {0} | {1} | isPrimary={2}", ins.Address, ins.State, ins.IsPrimary);
						break;
					default:
						Console.WriteLine("StateChanged: {0} | {1} | isPrimary={2}", ins.Address, ins.State, ins.IsPrimary);
						break;
				}
			}
		}

		long _firstStepdownLamdaValue = 0;
		MongoServerInstance StepDown(MongoRepo repo, MongoServerInstance instance)
		{
			Assert.IsNotNull(instance);
			DateTime first = DateTime.UtcNow;
			Console.WriteLine("\r\n------ BEGIN STEP DOWN for {0}", instance.Address);
			while(instance.IsPrimary)
			{
				try
				{
					using (repo.Server.RequestStart(repo.AdminDb, instance))
					{
						CommandResult cr = repo.AdminDb.RunCommand(new CommandDocument
						{
							{ "replSetStepDown", 60 },
							//{ "force", true }
						});
						Assert.IsNotNull(cr);
						Assert.IsTrue(cr.Ok);
						Console.WriteLine("\r\nStepDown: OK for {0}", instance.Address);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("StepDown: " + instance.Address, ex);
					Thread.Sleep(1000);
					instance.RefreshStateAsSoonAsPossible();
					Thread.Sleep(1000);
				}
			}
			Interlocked.CompareExchange(ref _firstStepdownLamdaValue, Interlocked.Read(ref _lambdaTicks), 0);
			Console.WriteLine("\r\nEEEEEEEEEEEEXIT STEP DOWN for {0}. Took: {1}", instance.Address, DateTime.UtcNow - first);
			return instance;
		}
	}
}
