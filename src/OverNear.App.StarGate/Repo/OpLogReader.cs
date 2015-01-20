using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Configuration;

using log4net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;

using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	public class OpLogReader : IOpLogReader, IDisposable
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		const string LOCAL_DB = "local", OPL = "oplog.rs";

		public event Action<IOpLogReader, BsonTimestamp> OnFoundNewTimestamp;

		readonly MongoUrl _dbPath;
		readonly bool _masterOnly;
		readonly bool _hostedDb;

		public VerboseLogLevel VerboseLog { get; set; }

		public OpLogReader(MongoUrl dbPath, bool masterOnly = false, bool hostedDb = false)
		{
			try
			{
				if (dbPath == null)
					throw new ArgumentNullException("dbPath");

				int serverCount = dbPath.Servers != null ? dbPath.Servers.Count() : 0;
				if (serverCount == 0)
					throw new ArgumentException("dbPath.Servers id blank or missing");
				if (masterOnly && dbPath.Servers.Count() > 1)
					throw new ArgumentException("when masterOnly is true, dbPath.Servers can not be > 1");

				_dbPath = dbPath;
				_masterOnly = masterOnly;
				_hostedDb = hostedDb;
				InitRepoAndInstance(reThrow:true);

				Interlocked.Exchange(ref _state, 0);
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.Debug("CTOR init ok");
			}
			catch (Exception ex)
			{
				_logger.Fatal("CTOR", ex);
				throw;
			}
		}

		readonly object _mslock = new object();
		int _maxSize = 8;
		/// <summary>
		/// Get or set connection size
		/// </summary>
		public int MaxPoolSize
		{
			get { lock(_mslock) return _maxSize; }
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

		readonly object _repolock = new object();
		MongoRepo<OpLogLine> _repo;
		MongoRepo<OpLogLine> Repo
		{
			get { lock (_repolock) return _repo; }
			//set { lock (_repolock) _repo = value; }
		}
		public IMongoRepo CurrentRepo
		{
			get { return Repo; }
		}

		readonly object _dbInstanceLock = new object();
		MongoServerInstance _dbInstance;
		MongoServerInstance DbInstance
		{
			get { lock (_dbInstanceLock) return _dbInstance; }
			set { lock (_dbInstanceLock) _dbInstance = value; }
		}

		int _initialCheck = 0;
		void InitRepoAndInstance(bool reConnect = false, bool reThrow = false)
		{
			try
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.Debug("InitRepoAndInstance begins for " + _dbPath);

				lock (_repolock)
				{
					if (Interlocked.CompareExchange(ref _initialCheck, 1, 0) == 0) //only do this once
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
							_logger.Debug("InitRepoAndInstance first run for " + _dbPath);

						_repo = new MongoRepo<OpLogLine>(_dbPath,
							serverSettings: sv =>
							{
								if (_masterOnly)
								{
									sv.ConnectionMode = ConnectionMode.Direct;
									sv.ReadPreference = ReadPreference.Primary;
								}
								else
								{
									sv.ConnectionMode = ConnectionMode.ReplicaSet;
									sv.ReadPreference = ReadPreference.PrimaryPreferred;
								}
								sv.MaxConnectionPoolSize = MaxPoolSize;
								sv.MinConnectionPoolSize = sv.MaxConnectionPoolSize / 2;
								sv.ConnectTimeout = TimeSpan.FromMinutes(5);
								sv.MaxConnectionIdleTime = TimeSpan.FromMinutes(30);
								sv.MaxConnectionLifeTime = TimeSpan.Zero;
								sv.WaitQueueSize = 10000;
								sv.WaitQueueTimeout = TimeSpan.FromSeconds(5);
								return sv;
							},
							defaultDbName: dbn => LOCAL_DB,
							defaultCollection: coln => OPL);

						if (!_repo.Server.DbExists(LOCAL_DB))
							throw new InvalidOperationException(LOCAL_DB + " db does not exist! Please connect to a Mongo ReplicaSet only! " + _dbPath);
						if (!_repo.Collection.Exists())
							throw new InvalidOperationException(OPL + " collection does not exist! Please connect to a Mongo ReplicaSet only! " + _dbPath);
						if (_repo.Server.Instances.IsNullOrEmpty())
							throw new InvalidOperationException("dbPath does not connect to multiple instances: " + _dbPath);
					}
					else
					{
						if(reConnect)
							_repo.Server.Reconnect();

						_repo.Server.VerifyState();
					}

					lock (_dbInstanceLock)
					{
						if (_dbInstance != null)
							_dbInstance.StateChanged -= Instance_StateChanged; //de-register existing instance if any..

						if (_repo.Server.Instances.IsNullOrEmpty())
							throw new ApplicationException("_repo.Server.Instances is null or empty!");

						if (_masterOnly) //implicit!
						{
							if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
								_logger.Debug("_masterOnly == TRUE");

							if(_hostedDb)
								_dbInstance = _repo.Server.Instances.FirstOrDefault(si => si.Address.ToString().Contains(_dbPath.Server.ToString()));
							else
								_dbInstance = _repo.Server.Instances.FirstOrDefault(si => string.Compare(_dbPath.Server.ToString(), si.Address.ToString(), true) == 0);
						}
						else
						{
							if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
								_logger.Debug("_masterOnly == FALSE");
							if (!_repo.Server.Instances.All(si => si.InstanceType == MongoServerInstanceType.ReplicaSetMember))
								throw new InvalidOperationException("dbPath does not connect to ReplicaSetMembers: " + _dbPath);

							_dbInstance = _repo.Server.Primary;
						}

						if (_dbInstance == null)
							throw new InvalidOperationException("Unable to attach to " + (_masterOnly ? "master" : "a single") + " instance!");
						else
							_dbInstance.StateChanged += Instance_StateChanged;
					}
				}
				if(VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.Info("InitRepoAndInstance completed for " + _dbPath);
			}
			catch (Exception ex)
			{
				_logger.Error("InitRepoAndInstance", ex);
				if(reThrow)
					throw;
			}
		}

		void Instance_StateChanged(object sender, EventArgs e)
		{
			try
			{
				string msg = string.Empty;
				if (sender != null && sender is MongoServerInstance)
				{
					MongoServerInstance ins = sender as MongoServerInstance;
					msg = string.Format("Instance_StateChanged: _masterOnly={0} | {1}={2} | isPrimary={3}", _masterOnly, ins.Address, ins.State, ins.IsPrimary);
					switch (ins.State)
					{
						case MongoServerState.Disconnecting:
						case MongoServerState.Disconnected:
							if (Repo != null)
							{
								msg += " RETRY CONNECT";
								if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
									_logger.Info(msg);

								Repo.Server.TryConnect();
							}
							break;
						default:
							if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
								_logger.Debug(msg);
							break;
					}
				}
				else if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
				{
					msg = string.Format("Instance_StateChanged: _masterOnly={0}", _masterOnly);
					_logger.Debug(msg);
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Instance_StateChanged", ex);
			}
		}

		int _state = -1;
		~OpLogReader() { Dispose(); }
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _state, 2, 1) == 1) //forcing state to 2 will cause early exit
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.Info("Dispose requested, attempt to stop.");

				ThreadPool.QueueUserWorkItem(o => 
				{
					try
					{
						Thread.Sleep(1000);
						if (Interlocked.CompareExchange(ref _readExit, 1, 0) == 0 && Repo != null && Repo.Server != null)
						{
							_logger.WarnFormat("Disposing {0}", _dbPath);
							Interlocked.Exchange(ref _state, 3); //force kill state...
							Repo.Server.Disconnect();
						}
					}
					catch (Exception ex)
					{
						_logger.Error("Dispose: InnerForceKill", ex);
					}
				});
			}
			else
				_logger.Warn("Dispose is not required. Logic never init completely or never started.");
		}

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

		long _noMaster = -1;
		int _readExit = 0;
		/// <summary>
		/// Start reading from a specific timestamp. 
		/// Timestamp ref is modified to last fetched value as function fetch next from db cursor. 
		/// bsonTS spec: <see cref="http://docs.mongodb.org/manual/reference/bson-types/"/>
		/// </summary>
		/// <param name="bsonTimeStamp">-1 will start at latest, 0 will start since beginging, any other value will be ts specific</param>
		public void Read(ref BsonTimestamp bsonTimeStamp, Action<OpLogLine> fetchNext)
		{
			if (fetchNext == null)
				throw new ArgumentNullException("fetchNext");
			if (DbInstance == null)
				throw new ApplicationException("DbInstance is some how null!");

			var original = new BsonTimestamp(bsonTimeStamp.Value);
			try
			{
				if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
				{
					if(VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
						_logger.InfoFormat("Read({0}, {1}) begins", bsonTimeStamp.ToDateTime(), fetchNext.Method.Name);

					if (bsonTimeStamp.Value <= 0) //continue latest...
						SetValue(ref bsonTimeStamp, GetLatestTimeStamp());
					while (Interlocked.CompareExchange(ref _state, 1, 1) == 1) //only continues when state is 1!
					{
						try
						{
							MongoServerInstance master = null;
							if (!DbInstance.IsPrimary && !_masterOnly && (master = Repo.Server.Primary) != null)
							{
								DbInstance.StateChanged -= Instance_StateChanged; //de-register
								(DbInstance = master).StateChanged += Instance_StateChanged; //re-register
							}
							if (DbInstance.IsPrimary)
							{
								long counts;
								if ((counts = Interlocked.Read(ref _noMaster)) > 0)
								{
									string wmsg = string.Format("Waking up from a long sleep: {0}. Already read {1:N0}. Restarted cursors: {2:N0}", 
										TimeSpan.FromSeconds(Settings.ReadFromAppConfig().NoMasterSleep.TotalSeconds * counts), 
										this.TotalReads, 
										this.CursorRestarts);
									_logger.Info(wmsg);
								}
								Interlocked.Exchange(ref _noMaster, -1);
							}
							else
								throw new NotMasterException("GetMaster fails: can not fetch master, going to sleep for awhile");

							using (Repo.Server.RequestStart(Repo.Database, DbInstance))
							{
								IMongoQuery q = Query<OpLogLine>.GT(o => o.TimeStamp, bsonTimeStamp); //first query...
								MongoCursor<OpLogLine> cursor = Repo.Collection.Find(q)
									.SetSortOrder(SortBy.Ascending("$natural"))
									.SetReadPreference(_repo.Server.Settings.ReadPreference)
									.SetFlags(QueryFlags.TailableCursor | QueryFlags.AwaitData | QueryFlags.NoCursorTimeout | ((QueryFlags)8));

								foreach (OpLogLine d in cursor)
								{
									try
									{
										if (d.TimeStamp.Value > bsonTimeStamp.Value)
											SetValue(ref bsonTimeStamp, new BsonTimestamp(d.TimeStamp.Value)); //copy new value over

										fetchNext(d);
									}
									catch (FatalReaderException bad)
									{
										_logger.Fatal(string.Format("Read inner loop [{0}] {1}", DbInstance.Address, fetchNext.Method.Name), bad); //log fatal, rethrow!
										throw;
									}
									catch (Exception iex) 
									{ 
										_logger.Error("Read inner loop: " + fetchNext.Method.Name, iex); 
									}
									finally 
									{ 
										long read = Interlocked.Increment(ref _reads);
#if DEBUG
										if (read % 100 == 0)
											_logger.DebugFormat("Read {0:N0} items so far...", read);
#endif
									}
								}
							}
						}
						catch (FatalReaderException) { throw; } //rethrow!
						catch (NotMasterException /* nmex */)
						{
							long mErrs = Interlocked.Increment(ref _noMaster);
							TimeSpan nms = Settings.ReadFromAppConfig().NoMasterSleep;

							if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
							{
								string msg = string.Format("Read outer loop (NotMasterException): {0}. ZZzzz {1}", fetchNext.Method.Name, nms);
								_logger.Debug(msg);
							}
							Thread.Sleep(nms); //sleep longer
							InitRepoAndInstance();
						}
						catch (System.IO.EndOfStreamException)
						{
							TimeSpan nds = Settings.ReadFromAppConfig().NoDataSleep;
							if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
								_logger.Info("EndOfStreamException (nothing to read). Going to sleep for " + nds);

							Thread.Sleep(nds);
							InitRepoAndInstance();
						}
						catch (Exception rex)
						{
							_logger.Error("Read outer loop: " + fetchNext.Method.Name, rex);
							InitRepoAndInstance(true);
						}
						finally
						{
							Interlocked.Increment(ref _cursorRestarts);
							Thread.Sleep(Settings.ReadFromAppConfig().CursorRestartSleep); //sleep a little...
						}
					}
				}
				else
					_logger.Warn("Read: _state is not 0. Exiting read!");
			}
			catch (Exception ex)
			{
				string serr = string.Format("Read: {0} @ {1}", original.ToDateTime(), bsonTimeStamp.ToDateTime());
#if DEBUG
				Console.WriteLine("\r\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\r\n" + serr + "\r\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\r\n");
#endif
				if (Interlocked.CompareExchange(ref _state, 4, 3) == 3) //force killed!
					_logger.Warn(serr + ".  Force exit from dbConnection drop", ex);
				else
				{
					_logger.Error(serr, ex);
					throw; //rethrow!
				}
			}
			finally { Interlocked.Exchange(ref _readExit, 1); }
		}

		BsonTimestamp GetLatestTimeStamp()
		{
			var tsl = new BsonTimestamp(0);
			MongoCursor<OpLogLine> latestCursor = Repo.Collection.FindAll()
				.SetReadPreference(ReadPreference.PrimaryPreferred) //tries to read from master...
				.SetSortOrder(SortBy.Descending("$natural"))
				.SetLimit(1);
			OpLogLine op = latestCursor.FirstOrDefault();
			if (op != null && op.TimeStamp != null)
				tsl = op.TimeStamp;

			return tsl;
		}

		long _reads = 0;
		public long TotalReads { get { return Interlocked.Read(ref _reads); } }

		long _cursorRestarts = 0;
		public long CursorRestarts { get { return Interlocked.Read(ref _cursorRestarts); } }

	}
}
