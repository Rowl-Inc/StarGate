using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;
using System.Configuration;

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
	/// This test forces a bootstrap from scratch and test if a slave can be fsync to stop freeze, read from then re-enable fssync and unfreeze
	/// </summary>
	[Ignore]
    [Explicit]
	[TestFixture, Category("Default")]
	public class Online_BootstrapReaderTests : IDisposable
	{
		[BsonIgnoreExtraElements]
		public class DummyPayload : IShardedById<int>
		{
			public DummyPayload() { Dummy = Guid.NewGuid(); }
			public DummyPayload(int id) : this() { Id = id; }

			[BsonId, BsonRepresentation(BsonType.Int32)]
			public int Id { get; set; } //random ids by design, but will test against insert order...

			[BsonElement]
			public Guid Dummy { get; set; }
		}

		readonly ICollection<int> _idHash = new CollectionTrigger<int>(new HashSet<int>());
		readonly Random _rand = new Random();
		readonly MongoRepo<DummyPayload> _repo;
		//readonly IList<BootstrapReader> _readers = new List<BootstrapReader>();

		readonly BootstrapReader _reader;
		readonly string _nameSpace;

		public Online_BootstrapReaderTests()
		{
			string name = typeof(DummyPayload).Name;

			var url = new MongoUrl(ConfigurationManager.AppSettings.ExtractConfiguration("UnitTest", string.Empty));
			_repo = new MongoRepo<DummyPayload>(url);
			StringAssert.AreNotEqualIgnoringCase(name, _repo.Database.Name);
			_nameSpace = _repo.Database.Name + '.' + name;

			_repo.Server.VerifyState();
			Assert.AreEqual(MongoServerState.Connected, _repo.Server.State);
			
			Assert.IsNotNull(_repo.Server.Instance);
			Assert.AreEqual(MongoServerInstanceType.ShardRouter, _repo.Server.Instance.InstanceType);

			//var pf = new PathFinder(url.ToString());
			//IList<MongoUrl> shards = pf.AllShards();
			//CollectionAssert.IsNotEmpty(shards);
			//foreach (MongoUrl sh in shards)
			//{
			//	Assert.IsNotNull(sh);
			//	var ub = new MongoUrlBuilder(sh.ToString());
			//	ub.DatabaseName = name;
			//	MongoUrl u = ub.ToMongoUrl();
			//	var reader = new BootstrapReader(u, new NsInfo(name +'.'+ name));
			//	reader.OnFoundNewTimestamp += _reader_OnFoundNewTimestamp;
			//	Assert.AreEqual(0, reader.TotalReads);
			//	_readers.Add(reader);
			//}
			//Assert.AreEqual(shards.Count, _readers.Count);

			if (_repo.Collection.Count() > 0)
			{
				Console.WriteLine("Removing leftovers");
				_repo.Collection.RemoveAll(WriteConcern.Acknowledged);
			}

			WriteDummyData();

			//NOTE: we moved to a single bootstrap thread model
			_reader = new BootstrapReader(url, true, false, new NsInfo(@"\." + name + '$'));
			_reader.OnFoundNewTimestamp += _reader_OnFoundNewTimestamp;
			Assert.AreEqual(0, _reader.TotalReads);
		}

		volatile BsonTimestamp _lastTs = new BsonTimestamp(0);
		void _reader_OnFoundNewTimestamp(IOpLogReader caller, BsonTimestamp ts)
		{
			Assert.Greater(ts.Value, _lastTs.Value);
			_lastTs = ts;
		}

		~Online_BootstrapReaderTests() { Dispose(); }
		int _dispoed = 0;

		[TestFixtureTearDown]
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _dispoed, 1, 0) == 0)
			{
				try
				{
					_repo.Collection.Drop();
					Thread.Sleep(1000);
				}
				catch (Exception ex) { Console.WriteLine("Dispose output: Collection.Drop()", ex); }
				try
				{
					_repo.Database.Drop();
				}
				catch (Exception ex) { Console.WriteLine("Dispose output: Db.Drop()", ex); }
			}
		}

		[Test]
		public void Test0_Throws()
		{
			//string name = "dummy";
			var url = new MongoUrl("mongodb://localhost/dummy");
			Assert.Throws<InvalidOperationException>(() => new BootstrapReader(url));
			NsInfo[] nn = null;
			Assert.Throws<InvalidOperationException>(() => new BootstrapReader(url, nn));
			Assert.Throws<InvalidOperationException>(() => new BootstrapReader(url, new NsInfo[0]));
			Assert.Throws<InvalidOperationException>(() => new BootstrapReader(url, new NsInfo[1] { null }));
			//Assert.Throws<InvalidOperationException>(() => new BootstrapReader(url, "oplog.rs"));
			Assert.Throws<InvalidOperationException>(() => 
				new BootstrapReader(
					new MongoUrl("mongodb://localhost/local"), 
					new NsInfo("test\\.test")));
		}

		[Explicit]
		[Category("ignore.online")]
		[Test]
		public void Test1_LockUnlock()
		{
			//for (int i = 0; i < _readers.Count; i++)
			{
				//BootstrapReader reader = _readers[i];

				BootstrapReader reader = _reader;
				MongoRepo rp = reader.GetRsRepo();
				Assert.IsNotNull(rp.Server);
				Assert.IsNotNull(rp.Server.Settings);
				Assert.AreEqual(ReadPreference.Secondary, rp.Server.Settings.ReadPreference);
				CollectionAssert.IsNotEmpty(rp.Server.Secondaries);

				MongoServerInstance slave = BootstrapReader.GetFirstAvailableSlave(rp);
				Assert.IsNotNull(slave);
				using (rp.Server.RequestStart(rp.Database, slave)) //use same connection!
				{
					try
					{
						Assert.IsFalse(reader.IsFrozenLocked(rp));
					}
					catch
					{
						reader.UnFreezeUnLock(rp);
						throw;
					}
					bool islocked = false;
					try
					{
						Assert.IsTrue(islocked = reader.FreezeAndLock(rp));
						Assert.IsTrue(reader.IsFrozenLocked(rp));
						Assert.IsFalse(islocked = !reader.UnFreezeUnLock(rp));
						Assert.IsFalse(reader.IsFrozenLocked(rp));
					}
					finally
					{
						if (islocked)
							reader.UnFreezeUnLock(rp);
					}
				}
			}
			Thread.Sleep(1000); //allow unlock cleanup
		}

		void WriteDummyData()
		{
			WriteDummyData(null);
		}

		const int OUTTER_LOOP = 2, INNER_LOOP = 10;
		void WriteDummyData(ICollection<int> savePayloadIds)
		{
			int missing = 0;
			long before = _repo.Collection.Count();
			for (int i = 0; i < OUTTER_LOOP; i++) //loop 10x
			{
				var batch = new List<DummyPayload>();
				for (int j = 0; j < INNER_LOOP; j++) //batch of 500 items each time
				{
					DummyPayload pl;
					if (MockPayload(out pl, savePayloadIds))
						batch.Add(pl);
					else
						missing++;
				}
				_repo.Collection.InsertBatch(batch, WriteConcern.Acknowledged); //ensure writes goes to all slaves!
			}
			_repo.Server.TryConnect();
			Thread.Sleep(100);
			long count = _repo.Collection.Count();
			if (savePayloadIds == null)
				savePayloadIds = _idHash;

			Assert.AreEqual(savePayloadIds.Count, count - before);
			Assert.AreEqual(OUTTER_LOOP * INNER_LOOP, (count + missing) - before);
		}

		bool MockPayload(out DummyPayload pl, ICollection<int> savePayloadIds = null)
		{
			if (savePayloadIds == null)
				savePayloadIds = _idHash;

			pl = new DummyPayload(_rand.Next());
			savePayloadIds.Add(pl.Id);
			return savePayloadIds.Contains(pl.Id);
		}

		[Test]
		public void Test2_Reads()
		{
			//WriteDummyData();
			Console.WriteLine("_idHash.count: {0}", _idHash.Count);
			Console.WriteLine("_idHash[0]: {0}", _idHash.First());

			long total = 0;
			//for (int i = 0; i < _readers.Count; i++)
			{
				//BootstrapReader reader = _readers[i];
				string i = "only";
				BootstrapReader reader = _reader;
				var ts = new BsonTimestamp(0);

				Console.WriteLine("Begin reader #{0}", i);
				reader.Read(ref ts, (op) => OplogLineFound(op, reader));
				Console.WriteLine("Completed _reader #" + i);

				long tt = Interlocked.Add(ref total, reader.TotalReads);
				Console.WriteLine("Complete reader #{0} found {1:N0} items, tally: {2:N0}", i, reader.TotalReads, tt);
			}
			Assert.AreEqual(_idHash.Count, Interlocked.Read(ref total));
			//Assert.AreEqual(_idHash.Count + _ammends.Count, _repo.Collection.Count());
			Assert.AreEqual(_idHash.Count, _repo.Collection.Count());

			//Console.WriteLine("Completed {0} _readers" + _readers.Count);
			Console.WriteLine("Completed _reader");
			//if (_sideInserts != null && _sideInserts.IsAlive)
			//	_sideInserts.Join(500);
		}

		long _lastCreated = DateTime.MinValue.Ticks;
		DateTime LastCreated
		{
			get { return new DateTime(Interlocked.Read(ref _lastCreated)); }
			set { Interlocked.Exchange(ref _lastCreated, value.Ticks); }
		}

		long _lastHash = 0;
		long _opsCount = 0;
		void OplogLineFound(OpLogLine op, BootstrapReader reader)
		{
			try
			{
				CollectionAssert.IsNotEmpty(_idHash);

				Assert.IsNotNull(reader);
				long oc = Interlocked.Increment(ref _opsCount);
				//Assert.AreEqual(oc - 1, reader.TotalReads, "oc-1");

				Assert.IsNotNull(op);
				Assert.Greater(op.Created, LastCreated, "_opsCount: " + oc);
				LastCreated = op.Created;

				Assert.AreNotEqual(Interlocked.Read(ref _lastHash), op.Hash);
				Interlocked.Exchange(ref _lastHash, op.Hash);

				//Assert.AreEqual(_nameSpace.Replace("_NUNIT.", "."), op.NameSpace);
				Assert.AreEqual(_nameSpace, op.NameSpace);
				Assert.AreEqual(OpLogType.Insert, op.Operation);
				Assert.AreEqual(1, op.Version);
				Assert.AreEqual(_lastTs, op.TimeStamp);

				BsonDocument p = op.Payload;
				Assert.IsNotNull(p);
				Assert.IsTrue(p.Contains("_id"));
				BsonValue v = p["_id"];
				Assert.IsNotNull(v);
				Assert.IsTrue(v.IsInt32);
				int r = v.AsInt32;
				CollectionAssert.Contains(_idHash, r, "oc #{0} id: {1}", oc, r); 

				//if (oc == 1) //first run!
				//{
				//	_sideInserts = new Thread(WriteMoreDummyData) { IsBackground = true, Name = "MoData" };
				//	_sideInserts.Start();
				//}
				//Assert.IsFalse(_ammends.Contains(r));
				if (oc % 2 == 0)
					Thread.Sleep(1);
			}
			catch (Exception ex)
			{
				//Console.WriteLine(ex.Message);
				throw new FatalReaderException("OplogLineFound", ex);
			}
		}

		//readonly ICollection<int> _ammends = new CollectionTrigger<int>(new HashSet<int>());
		//Thread _sideInserts;
		//void WriteMoreDummyData()
		//{
		//	Console.WriteLine("WriteMoreDummyData started");

		//	CollectionAssert.IsEmpty(_ammends);
		//	WriteDummyData(_ammends);
		//	CollectionAssert.IsNotEmpty(_ammends);
		//	Assert.Less(Math.Abs(_idHash.Count - _ammends.Count), INNER_LOOP / 10);
		//}
	}
}
