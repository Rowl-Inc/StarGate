using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using log4net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;

using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	public class BootstrapReader : IOpLogReader
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		static readonly HashSet<string> IGNORE_DB = new HashSet<string>
		{
			"local",
			"config",
			//"admin",
		};
		public event Action<IOpLogReader, BsonTimestamp> OnFoundNewTimestamp;

		readonly NsInfo[] _nsInfos = new NsInfo[0];
		internal readonly MongoUrl _dbPath;
		readonly bool _singleInstanceMode = false;
		readonly bool _hostedDb = false;

		public BootstrapReader(MongoUrl dbPath, params NsInfo[] nsInfos)
			: this(dbPath, false, false, nsInfos)
		{
		}
		public BootstrapReader(MongoUrl dbPath, bool singleInstance, bool hostedDb, params NsInfo[] nsInfos)
		{
			if (dbPath == null)
				throw new ArgumentNullException("dbPath");
			if (IGNORE_DB.Contains(dbPath.DatabaseName))
				throw new InvalidOperationException("dbPath.DatabaseName can not be: " + dbPath.DatabaseName);

			_hostedDb = hostedDb;
			_dbPath = dbPath;
			MongoRepo rp = GetRsRepo(_dbPath);
			if (singleInstance || rp.Server.Instances.IsNullOrEmpty())
			{
				_singleInstanceMode = true;
				_logger.WarnFormat("Running in SINGLE INSTANCE MODE! {0}", _dbPath);
			}
			else if (!rp.Server.Instances.All(si => si.InstanceType == MongoServerInstanceType.ReplicaSetMember))
				throw new InvalidOperationException("dbPath does not connect to ReplicaSetMembers: " + _dbPath);

			if (!nsInfos.IsNullOrEmpty())
			{
				_nsInfos = (from ns in GetTrueNsInfo(rp, nsInfos)
							where ns != null
							group ns by ns.ToString() into ng
							select ng.FirstOrDefault()).ToArray();
			}
			if(_nsInfos.IsNullOrEmpty())
				_logger.Warn("collectionNames is empty or does not contain a single valid item");

			_logger.Debug("CTOR init ok");
		}

		public VerboseLogLevel VerboseLog { get; set; }

		readonly object _mslock = new object();
		int _maxSize = 8;
		/// <summary>
		/// Get or set connection size
		/// </summary>
		public int MaxPoolSize
		{
			get { lock (_mslock) return _maxSize; }
			set
			{
				int v = value;
				if (v > 100)
					v = 100;
				else if (v < 10)
					v = 10;

				lock (_mslock)
				{
					_maxSize = v;
				}
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("MaxPoolSize is set at {0:N0}", v);
			}
		}

		ICollection<NsInfo> GetTrueNsInfo(MongoRepo rp, ICollection<NsInfo> rawInfos)
		{
			var infos = new List<NsInfo>();
			if (!rawInfos.IsNullOrEmpty())
			{
				var allNs = new HashSet<string>();
				{
					if (_hostedDb || LoopThroughAllDbs(rp, allNs) == LoopResponse.NoPermission)
						LoopThroughCurrentDb(rp, allNs);
				}
				if (!allNs.IsNullOrEmpty())
				{
					Regex ignoreNs = new Regex(Settings.ReadFromAppConfig().IgnoreNameSpace, RegexOptions.Compiled);
					foreach (NsInfo ns in rawInfos)
					{
						Regex re = new Regex(ns.Raw, RegexOptions.Compiled);
						foreach (string n in allNs)
						{
							if (!ignoreNs.IsMatch(n))
							{
								if (re.IsMatch(n))
									infos.Add(new NsInfo(n));
							}
						}
					}
				}
			}
			return infos;
		}

		enum LoopResponse
		{
			Unknown,
			OK,
			Error,
			NoPermission,
		}

		LoopResponse LoopThroughAllDbs(MongoRepo rp, HashSet<string> allNs)
		{
			LoopResponse r = LoopResponse.Unknown;
			try
			{
				string[] dbs = rp.Server.GetDatabaseNames().ToArray();
				foreach (string db in dbs)
				{
					if (IGNORE_DB.Contains(db))
						continue;

					string[] collections = rp.Server.GetDatabase(db).GetCollectionNames().ToArray();
					collections.ForEach(c => allNs.Add(db + '.' + c));
				}
				r = LoopResponse.OK;
			}
			catch (Exception ex)
			{
				allNs.Clear();
				const string NO_PERMISSION = @"not authorized on admin to execute command";
				if (ex.Message.Contains(NO_PERMISSION))
				{
					r = LoopResponse.NoPermission;
					_logger.ErrorFormat("LoopThroughAllDbs(...): {0}", NO_PERMISSION);
				}
				else
				{
					r = LoopResponse.Error;
					_logger.Error("LoopThroughAllDbs", ex);
				}
			}
			return r;
		}

		LoopResponse LoopThroughCurrentDb(MongoRepo rp, HashSet<string> allNs)
		{
			LoopResponse r = LoopResponse.Unknown;
			try
			{
				string db = rp.Database.Name;
				if (!IGNORE_DB.Contains(db))
				{
					string[] collections = rp.Server.GetDatabase(db).GetCollectionNames().ToArray();
					collections.ForEach(c => allNs.Add(db + '.' + c));
				}
				r = LoopResponse.OK;
			}
			catch (Exception ex)
			{
				allNs.Clear();
				r = LoopResponse.Error;
				_logger.Error("LoopThroughCurrentDb", ex);
			}
			return r;
		}

		enum RsState : int
		{
			NotStarted = 0,
			DetachBegin = 1,
			DetachEnd = 2,
			AttachBegin = 3,
			AttachEnd = 4,
			Complete = 5,
			Disposed = 6,
		}

		#region dispose, dtor & state helpers

		int _state = (int)RsState.NotStarted;
		bool SetIfTrue(RsState newState, RsState currentState)
		{
			int ns = (int)newState;
			int cr = (int)currentState;
			return Interlocked.CompareExchange(ref _state, ns, cr) == cr;
		}
		RsState SetState(RsState state)
		{
			int s = Interlocked.Exchange(ref _state, (int)state);
			return (RsState)s;
		}
		~BootstrapReader() { Dispose(); }
		public void Dispose()
		{
			if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
				_logger.Info("Dispose requested, attempt to stop.");

			Interlocked.Exchange(ref _state, (int)RsState.Disposed);
		}

		#endregion

		#region DetatchFromReplSet(...), ReAttachToRelpSet(...) & helpers

		const int ONE_WHOLE_DAY_IN_SECONDS = 60 * 60 * 24; //1 whole day
		bool DetatchFromReplSet(MongoRepo rp)
		{
			bool ok = false;
			if (SetIfTrue(RsState.DetachBegin, RsState.NotStarted))
			{
				try
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
						_logger.Debug("DetatchFromReplSet begins");

					FreezeAndLock(rp);
					ok = SetIfTrue(RsState.DetachEnd, RsState.DetachBegin);
					Thread.Sleep(100); //wait for lock...

					_logger.Info("DetatchFromReplSet successful");
				}
				catch (Exception ex)
				{
					_logger.Error("DetatchFromReplSet", ex);
					SetIfTrue(RsState.NotStarted, RsState.DetachBegin);
					throw;
				}
			}
			else
				_logger.Warn("DetatchFromReplSet failed: state is NotStarted!");
			return ok;
		}

		bool ReAttachToRelpSet(MongoRepo rp)
		{
			bool ok = false;
			if (SetIfTrue(RsState.AttachBegin, RsState.DetachEnd))
			{
				try
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
						_logger.Debug("ReAttachToRelpSet begins");

					UnFreezeUnLock(rp);
					ok = SetIfTrue(RsState.AttachEnd, RsState.AttachBegin);
					Thread.Sleep(20); //wait for unlock...
					_logger.Info("ReAttachToRelpSet successful");
				}
				catch (Exception ex)
				{
					_logger.Error("ReAttachToRelpSet", ex);
					SetIfTrue(RsState.DetachEnd, RsState.AttachBegin);
					throw;
				}
			}
			else
				_logger.Warn("ReAttachToRelpSet failed: state is not DetachEnd");
			return ok;
		}

		/// <see cref="http://docs.mongodb.org/manual/reference/command/fsync/"/>
		internal bool FreezeAndLock(MongoRepo rp)
		{
			if (rp == null)
				throw new ArgumentNullException("rp");

			try
			{
				bool freezeOk = false;
				CommandResult fcr = rp.AdminDb.RunCommand(new CommandDocument("replSetFreeze", ONE_WHOLE_DAY_IN_SECONDS));
				freezeOk = fcr.Ok;
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("FreezeAndLock: replSetFreeze({0}) -> {1}", ONE_WHOLE_DAY_IN_SECONDS, fcr);

				CommandResult scr = rp.AdminDb.RunCommand(new CommandDocument
				{
					{ "fsync", 1 },
					{ "lock", true },
					{ "async", false }, //force flush immediately...
				});
				bool syncLockOk = scr.Ok;
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("FreezeAndLock: fsync(1) -> {0}", scr);
				//return syncLockOk;

				return freezeOk && syncLockOk;
			}
			catch (Exception lockEx)
			{
				_logger.Error("FreezeAndLock", lockEx);
				UnFreezeUnLock(rp);
				throw;
			}
		}

		/// <see cref="https://github.com/mongodb/mongo-java-driver/commit/cdb25d8789f6d36cfede9eabd00ee94261dc570a"/>
		internal bool UnFreezeUnLock(MongoRepo rp)
		{
			if (rp == null)
				throw new ArgumentNullException("rp");

			try
			{
				//MongoCollection<BsonDocument> mc = rp.AdminDb.GetSisterDatabase("admin").GetCollection<BsonDocument>("$cmd.sys.unlock");
				MongoCollection<BsonDocument> mc = rp.AdminDb.GetCollection<BsonDocument>("$cmd.sys.unlock");
				BsonDocument r = mc.FindOne();

				//BsonValue v = rp.AdminDb.Eval("db.fsyncUnlock();");
				//BsonDocument r = v != null && v.IsBsonDocument ? v.AsBsonDocument : null;

				bool unLockOk = r != null && r.Contains("ok") && r["ok"].ToString() == "1";
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("UnFreezeUnLock: $cmd.sys.unlock -> {0}", r);
				//return unLockOk;

				bool unFreezeOk = false;
				CommandResult fcr = rp.AdminDb.RunCommand(new CommandDocument("replSetFreeze", 0));
				unFreezeOk = fcr.Ok;
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("UnFreezeUnLock: replSetFreeze(0) -> {0}", fcr);
				return unFreezeOk && unLockOk;
			}
			catch (Exception lockEx)
			{
				_logger.Error("UnFreezeUnLock", lockEx);
				throw;
			}
		}

		internal bool IsFrozenLocked(MongoRepo rp)
		{
			if (rp == null)
				throw new ArgumentNullException("rp");

			try
			{
				MongoCollection<BsonDocument> mc = rp.AdminDb.GetCollection<BsonDocument>("$cmd.sys.inprog");
				BsonDocument r = mc.FindOne();
				bool OK = r != null && r.Contains("fsyncLock") && r["fsyncLock"].ToString() == "true";
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("IsFrozenLocked: $cmd.sys.inprog -> {0}", r);

				return OK;
			}
			catch (Exception fex)
			{
				_logger.Error("IsFrozenLocked", fex);
				throw;
			}
		}

		#endregion

		long _reads = 0;
		public long TotalReads { get { return Interlocked.Read(ref _reads); } }

		bool AllSlavesAreUp(MongoRepo repo)
		{
			bool ok = false;

			CommandResult cr = repo.AdminDb.RunCommand("replSetGetStatus");
			if (cr != null && cr.Response != null && cr.Response.Contains("members"))
			{
				BsonDocument[] allNodes = (from v in cr.Response["members"].AsBsonArray
										   where v.IsBsonDocument
										   select v.AsBsonDocument).ToArray();
				if (!allNodes.IsNullOrEmpty())
				{
					BsonDocument primary = (from d in allNodes
											where d["stateStr"].AsString == "PRIMARY"
											select d).FirstOrDefault();
					BsonDocument[] secondaries = (from d in allNodes
												  where d["stateStr"].AsString == "SECONDARY"
												  select d).ToArray();
					if (primary != null && !secondaries.IsNullOrEmpty())
					{
						BsonDocument[] goodNodes = (from d in allNodes
													where d["health"].ToString() == "1" &&
													   d["state"].ToString() == "2" &&
													   (!d.Contains("lastHeartbeatMessage") || !d.Contains("errmsg"))
													select d).ToArray();
						ok = secondaries.Length == goodNodes.Length;
					}
				}
			}
			return ok;
		}

		void EnsureAllNodesRunning(MongoRepo rp)
		{
			int waitCount = 0;
			while (!AllSlavesAreUp(rp))
			{
				Thread.Sleep(1000);
				waitCount++;
				if (waitCount % 10 == 0)
					Console.WriteLine("EnsureAllNodesRunning: Waiting for all slaves to come online...");
			}
		}

		class ReadParam
		{
			public MongoRepo Repo;
			public MongoServerInstance Instance;
			public BsonTimestamp TimeStamp;
			public Action<OpLogLine> FetchNext;
			public NsInfo NS;
		}


		readonly object _crlock = new object();
		IMongoRepo _curRepo;
		public IMongoRepo CurrentRepo
		{
			get { lock(_crlock) return _curRepo; }
			set
			{
				if (value == null)
					throw new ArgumentNullException();

				lock (_crlock) { _curRepo = value; }
			}
		}

		public void Read(ref BsonTimestamp bsonTimeStamp, Action<OpLogLine> fetchNext)
		{
			if (fetchNext == null)
				throw new ArgumentNullException("fetchNext");

			_logger.InfoFormat("Read(ref {0}, {1}) begins", bsonTimeStamp.ToDateTime(), fetchNext.Method.Name);
			bool detatched = false;
			MongoRepo rp = GetRsRepo();

			if(!_singleInstanceMode)
				EnsureAllNodesRunning(rp);

			MongoServerInstance node;
			if(_singleInstanceMode)
				node = GetMaster(rp);
			else
				node = GetFirstAvailableSlave(rp);
			using (rp.Server.RequestStart(rp.Database, node)) //setup environment
			{
//#if CLEAR
				if (IsFrozenLocked(rp))
					ReAttachToRelpSet(rp);
//#endif
				if(!_singleInstanceMode)
					detatched = DetatchFromReplSet(rp);
			}

			try
			{
				if (_singleInstanceMode || detatched)
				{
					int bc = 0;
					Parallel.For(0, _nsInfos.Length, new ParallelOptions { MaxDegreeOfParallelism = 10 }, i =>
					//foreach (NsInfo ns in _nsInfos) //do the actual work here....
					{
						NsInfo ns = _nsInfos[i];
						try
						{
							int c = Interlocked.Increment(ref bc);
							_logger.DebugFormat("Bootstrapping {0}/{1}: {2}", c, _nsInfos.Length, ns);
							var ub = new MongoUrlBuilder(_dbPath.ToString());
							ub.DatabaseName = ns.Database;

							MongoRepo nrp = GetRsRepo(ub.ToMongoUrl());
							ReadCollection(new ReadParam
							{
								Repo = nrp,
								Instance = node,
								NS = ns,
								FetchNext = fetchNext,
								TimeStamp = DateTime.UtcNow.ToTimestamp(), //mocked...
							});

							_logger.InfoFormat("Bootstrap COMPLETED! {0}/{1}: {2}", c, _nsInfos.Length, ns);
						}
						catch (Exception iex)
						{
							_logger.Error(ns.ToString(), iex);
						}
					});

					//normal cleanup work...
					bool reattach;
					if (_singleInstanceMode)
						reattach = true;
					else
					{
						using (rp.Server.RequestStart(rp.Database, node))
							reattach = ReAttachToRelpSet(rp);
					}
					if (reattach)
					{
						detatched = false;
						BsonTimestamp lts = ReadLastTs(rp);
						SetValue(ref bsonTimeStamp, lts); //do this at the end...
						Dispose(); //dispose anyway at this point...
						_logger.InfoFormat("Read(ref {0}, {1}) exit!", bsonTimeStamp.ToDateTime(), fetchNext.Method.Name);
					}
					else
						throw new ApplicationException("Unable to re-attach Secondary back to ReplicaSet");
				}
				else
					throw new ApplicationException("Unable to detatch Secondary from ReplicaSet");
			}
			catch (Exception ex)
			{
				_logger.Error(string.Format("Read(ref {0}, {1})", bsonTimeStamp, fetchNext.Method.Name), ex);
				throw;
			}
			finally
			{
				if (!_singleInstanceMode && detatched && node != null) //emergency cleanup work...
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
						_logger.Info("Read: ReAttachToRelpSet(...) cleanup");

					using (rp.Server.RequestStart(rp.Database, node))
						ReAttachToRelpSet(rp);
				}
			}
		}

		const int ITEMS_PROGRESS_LOG = 10000; //log status once every count
		long ReadCollection(ReadParam ci)
		{
			if (ci == null)
				throw new ArgumentNullException("ci");

			if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
				_logger.DebugFormat("ReadCollection(...) for {0} acquiring mongo collection: {1}", ci.FetchNext.Method.Name, ci.NS);

			DateTime started = DateTime.UtcNow;
			long itemsRead = 0;
			using (ci.Repo.Server.RequestStart(ci.Repo.Database, ci.Instance))
			{
				MongoCollection mc = ci.Repo.Database.GetCollection(ci.NS.Collection);
				MongoCursor<BsonDocument> cursor = mc.FindAllAs<BsonDocument>();

				_logger.InfoFormat("ReadCollection(...) for {0}/{1} enters loop {2}", ci.Instance.Address, ci.NS, ci.FetchNext.Method.Name);
				foreach (BsonDocument d in cursor)
				{
					try
					{
						OpLogLine op = MockOplog(d, ci.NS.ToString());
						if (op.TimeStamp.Value > ci.TimeStamp.Value)
							SetValue(ref ci.TimeStamp, op.TimeStamp);

						ci.FetchNext(op);

						if (_logger.IsDebugEnabled && itemsRead % ITEMS_PROGRESS_LOG == 0)
							_logger.DebugFormat("ReadCollection for {0} @ {1:N0} items", ci.NS.Collection, itemsRead);

						itemsRead++;
					}
					catch (FatalReaderException bad)
					{
						string msg = string.Format("ReadCollection (loop {0}): {1}", ci.NS.Collection, ci.FetchNext.Method.Name);
						_logger.Fatal(msg, bad); //log fatal, rethrow!
						throw;
					}
					catch (Exception iex) 
					{
						string msg = string.Format("ReadCollection (loop {0}): {1}", ci.NS.Collection, ci.FetchNext.Method.Name);
						_logger.Error(msg, iex); 
					}
					finally 
					{ 
						Interlocked.Increment(ref _reads); 
					}
				}
			}
			ci.Repo.Server.Disconnect();
			_logger.InfoFormat("ReadCollection(...) for {0}/{1} completed {2:N0} items read. Took {3}", ci.Instance.Address, ci.NS, itemsRead, DateTime.UtcNow - started);
			return itemsRead;
		}


		long _lastCreated = DateTime.MinValue.Ticks;
		DateTime LastCreated //threadsafe
		{
			get { return new DateTime(Interlocked.Read(ref _lastCreated)); }
			set { Interlocked.Exchange(ref _lastCreated, value.Ticks); }
		}

		int _increment = 0; //threadsafe increments
		OpLogLine MockOplog(BsonDocument d, string nameSpace)
		{
			DateTime now = DateTime.UtcNow;
			int inc;
			if (now >= LastCreated)
				inc = Interlocked.Increment(ref _increment);
			else
			{
				Interlocked.Exchange(ref _increment, inc = 0);
				LastCreated = now;
			}

			int epox = (int)now.ToUnixTime();
			var op = new OpLogLine //mock oplog to emulate an insert
			{
				Operation = OpLogType.Insert,
				Payload = d,
				NameSpace = nameSpace,
				TimeStamp = new BsonTimestamp(epox, inc),
				Version = 1,
			};
			if (d.Contains("_id") && d["_id"].IsObjectId)
			{
				ObjectId oid = d["_id"].AsObjectId;
				op.Created = oid.CreationTime;
			}
			else
				op.Created = now;

			op.Created = op.Created.AddMilliseconds(inc);

			var arr = new byte[8];
			BitConverter.GetBytes(epox).CopyTo(arr, 0);
			BitConverter.GetBytes(op.GetHashCode()).CopyTo(arr, 4);
			op.Hash = BitConverter.ToInt64(arr, 0);

			return op;
		}

		#region Read(...) helpers

		void SetValue(ref BsonTimestamp currentTs, BsonTimestamp newTs)
		{
			try
			{
				currentTs = newTs;
				if (OnFoundNewTimestamp != null)
					OnFoundNewTimestamp(this, currentTs);
			}
			catch (Exception ex)
			{
				_logger.Error("SetValue", ex); //silence error...
			}
		}

		internal MongoRepo GetRsRepo(MongoUrl dbPath = null)
		{
			MongoUrl u = dbPath ?? _dbPath;
			//if (string.IsNullOrWhiteSpace(u.DatabaseName))
			//	throw new ArgumentException("dbPath.DatabaseName is null or missing");

			var rp = new MongoRepo(u, serverSettings: ss =>
			{
				if (_singleInstanceMode)
					ss.ReadPreference = ReadPreference.Primary;
				else
					ss.ReadPreference = ReadPreference.Secondary;

				ss.MaxConnectionPoolSize = MaxPoolSize;
				ss.MinConnectionPoolSize = ss.MaxConnectionPoolSize / 2;
				ss.ConnectTimeout = TimeSpan.FromSeconds(5);
				ss.MaxConnectionIdleTime = TimeSpan.FromSeconds(20);
				ss.MaxConnectionLifeTime = TimeSpan.FromMinutes(20);
				ss.WaitQueueSize = 1000;
				ss.WaitQueueTimeout = TimeSpan.FromSeconds(5);
				return ss;
			});
			switch (rp.Server.State)
			{
				case MongoServerState.Connected:
				case MongoServerState.Connecting:
					break;
				default:
					rp.Server.TryConnect(true);
					break;
			}
			rp.Server.VerifyState();
			CurrentRepo = rp;
			return rp;
		}

		internal static MongoServerInstance GetFirstAvailableSlave(MongoRepo repo)
		{
			if (repo.Server.Secondaries.IsNullOrEmpty())
				throw new ApplicationException("DetatchFromReplSet fails: No slave to detach!");

			MongoServerInstance slave = repo.Server.Secondaries.FirstOrDefault(sl =>
			{
				switch (sl.State)
				{
					case MongoServerState.Connected:
					case MongoServerState.ConnectedToSubset:
					case MongoServerState.Connecting:
						return sl.IsPassive || sl.IsSecondary;
					case MongoServerState.Disconnecting:
					case MongoServerState.Disconnected:
					default:
						return false;
				}
			});
			if (slave == null)
				throw new ApplicationException("GetFirstAvailableSlave fails: No connected slave to detatch!");
			else
				_logger.InfoFormat("GetFirstAvailableSlave(...) found: {0} -> {1}", slave.Address, slave.Settings);
			return slave;
		}

		internal static MongoServerInstance GetMaster(MongoRepo repo)
		{
			if (repo.Server.Primary == null)
				throw new ApplicationException("GetMaster can not find an available primary node");

			MongoServerInstance master = repo.Server.Primary;
			_logger.InfoFormat("GetMaster(...) found: {0} -> {1}", master.Address, master.Settings);
			return master;
		}

		const string OPLOG_COL = "oplog.rs";
		const string LOCAL_DB = "local";
		BsonTimestamp ReadLastTs(MongoRepo rp)
		{
			BsonTimestamp r = null;
			if (rp == null)
				throw new ArgumentNullException("rp");

			DateTime n = DateTime.UtcNow;
			try
			{
				if (rp.Server.Instances.Count() == 1 && rp.Server.Instance.InstanceType == MongoServerInstanceType.ShardRouter)
					r = ReadCurrentServerTime(rp);
				else
				{
					MongoDatabase local = rp.Server.GetDatabase(LOCAL_DB);
					MongoCollection<OpLogLine> col = local.GetCollection<OpLogLine>(OPLOG_COL);
					OpLogLine last = col.FindAll()
						.SetSortOrder(SortBy.Descending("$natural"))
						.SetLimit(1)
						.FirstOrDefault();

					if (last == null)
						r = ReadCurrentServerTime(rp);
					else
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
							_logger.InfoFormat("ReadLastTs returns: {0}", last);

						r = last.TimeStamp;
					}
				}
			}
			catch (Exception ex)
			{
				_logger.WarnFormat("ReadLastTs", ex);
				r = ReadCurrentServerTime(rp);
			}
			finally
			{
				if (r == null || r.Value <= 0)
				{
					_logger.WarnFormat("ReadLastTs unable to determine db server time, will use app server time {0}", n);
					r = ToBsonTimestamp(n);
				}
			}
			return r;
		}

		BsonTimestamp ReadCurrentServerTime(MongoRepo rp)
		{
			if (rp == null)
				throw new ArgumentNullException("rp");

			CommandResult cr = rp.AdminDb.RunCommand(new CommandDocument
			{
				{ "isMaster", 1 }
			});
			if (cr != null && cr.Ok && cr.Response != null && cr.Response.Contains("localTime"))
			{
				BsonDateTime dt = cr.Response["localTime"].AsBsonDateTime;
				return ToBsonTimestamp(dt.ToUniversalTime());
			}
			else
				return null;
		}

		static BsonTimestamp ToBsonTimestamp(DateTime dt)
		{
			int epox = dt.ToUniversalTime().ToUnixTime();
			var ts = new BsonTimestamp(epox, 0);
			return ts;
		}

		#endregion

	}
}
