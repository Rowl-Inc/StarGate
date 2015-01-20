using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoDB.Driver.Builders;

using log4net;

namespace OverNear.Infrastructure
{
    public class MongoRepo<T> : MongoRepo, IMongoRepo<T>
    {
        /// <summary>
        /// Maintains whether the singleton should not/cannot be initialized (-1), has not been initialized (0), or has been initialized (1)
        /// Thread safe comparison by Interlocked.CompareExchange
        /// </summary>
        static int _initSingleton = -1;
		readonly static TimeSpan SLOW_INDEX = TimeSpan.FromSeconds(15); //hardcode

		/// <summary>
		/// Low level singleton init control method, not meant for normal use
		/// This method give the power to skip or re-bootstrap data
		/// </summary>
		/// <param name="newValue">new int value. -1 will cause the next action to bootstrap and any other value will mean no bootstrap</param>
		/// <returns>new value</returns>
		public static int SetInitSingletonValue(int newValue)
		{
			return Interlocked.Exchange(ref _initSingleton, newValue);
		}

        /// <summary>
        /// Action method is called when the Collection object is first accessed (NOT on construction as originally implemented)
        /// </summary>
        public event Action<IMongoRepo<T>> SingletonInit;

        readonly MongoCollectionSettings _cfg;
        public virtual MongoCollectionSettings Settings { get { return _cfg; } }

        public virtual MongoCollection<T> Collection 
        { 
            get
            {
                if (SingletonInit != null && Interlocked.CompareExchange(ref _initSingleton, 1, 0) == 0) //only happens once ever
                { 
                    try
                    {
                        _logger.DebugFormat("SingletonInit for MongoRepo<{0}>", typeof(T).Name);
						ShardKeyEnsure();
                        SingletonInit(this);
                        _logger.InfoFormat("SingletonInit for MongoRepo<{0}> Completed", typeof(T).Name);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("Collection.SingletonInit", e);
                        throw;
                    }
                }
                return Database.GetCollection<T>(_cfName, Settings);
            } 
        }

		static readonly MongoUrl NULL_URL = null;

        /// <summary>
        /// Instantiate with appConfigKey that store the mongo connection string value. 
        /// Value should be mongoUrl connection string
        /// NOTE: use #collection_name to override collection in configuration (instead of using object names)
        /// </summary>
        /// <param name="appConfigKey">appConfig entry Key</param>
        /// <param name="serverSettings">lambda function to override parsed settings</param>
        public MongoRepo(string appConfigKey,
            Func<MongoServerSettings, MongoServerSettings> serverSettings = null,
            Func<MongoDatabaseSettings, MongoDatabaseSettings> databaseSettings = null,
			Func<string, string> defaultDbName = null,
			Func<string, string> defaultCollection = null)
			: this(ConfigurationManager.AppSettings.ExtractValue(appConfigKey, NULL_URL), serverSettings, databaseSettings, defaultDbName, defaultCollection)
        {
        }

		readonly string _cfName;
		public MongoRepo(MongoUrl connectionString,
			Func<MongoServerSettings, MongoServerSettings> serverSettings = null,
			Func<MongoDatabaseSettings, MongoDatabaseSettings> databaseSettings = null,
			Func<string, string> defaultDbName = null,
			Func<string, string> defaultCollection = null) :
			base(connectionString, serverSettings, databaseSettings, defaultDbName)
		{
			try
			{
				Tuple<string, string> splitUri = ExtractCollectionOverride(connectionString.ToString());
				string cfName;
				if (!string.IsNullOrWhiteSpace(splitUri.Item2))
					cfName = splitUri.Item2;
				else
				{
					string t = typeof(T).Name;
					int ix = t.ToLower().LastIndexOf("dbo");
					if (ix > 0 && ix + 3 == t.Length)
						t = t.Remove(ix);

					cfName = t;
				}
				_logger.InfoFormat("Connecting to {0}/{1}#{2}", connectionString.Servers.FirstOrDefault(), connectionString.DatabaseName, cfName);
				if (defaultCollection != null)
				{
					string col = defaultCollection(cfName);
					if (string.IsNullOrWhiteSpace(col))
						throw new InvalidOperationException("defaultCollection can not return null or empty collection name");

					cfName = col;
				}
				if (cfName != "oplog.rs" && VALID_MONGO_NAMES.IsMatch(cfName))
					cfName = VALID_MONGO_NAMES.Replace(cfName, "_");

				_cfName = cfName;
				MongoDatabaseSettings dbs = Database.Settings;
				_cfg = dbs.ExtractCollectionSettings();
			}
			catch (Exception ex)
			{
				_logger.Fatal("CTOR", ex);
				throw;
			}
			Interlocked.CompareExchange(ref _initSingleton, 0, -1); //Set initSingleton to 0 ONLY when value is -1 to indicate that it is ready to initialize.
		}

        #region ctor helpers

        static int _shardKeyEnsure = 0;
        void ShardKeyEnsure() //auto shard index
        {
            try
            {
                if (Interlocked.CompareExchange(ref _shardKeyEnsure, 1, 0) == 0) //ensure shardkey...
                {
                    Type t = typeof(T);
                    //var timer = new WallClockTimer("ShardKeyEnsure:" + t.Name);
                    bool enter = false;
                    try
                    {
						switch (Server.State)
						{
							case MongoServerState.Disconnected:
							case MongoServerState.Disconnecting:
								_logger.DebugFormat("Connection attempt for {0}#{1} -> {2}", _dbName, _cfName, _cfg.ToJson());
								Server.Connect();
								break;
						}
						if (Server.Instances.FirstOrDefault().InstanceType == MongoServerInstanceType.Unknown)
						{
							_logger.DebugFormat("Connection verify state for {0}#{1} -> {2}", _dbName, _cfName, _cfg.ToJson());
							Server.VerifyState();
						}

						bool isSharded = Server.Instances.FirstOrDefault().InstanceType == MongoServerInstanceType.ShardRouter;
						//if(!isSharded)
						//	_logger.Warn("ShardKeyEnsure: Current connection is not of type ShardRouter, no shardkey index ensured.");

						Type ts; //Check interface type to determine shard type
						if (enter = MatchShardRegEx(t, SHARDED_BY_SH_RE, out ts))
						{
							if (isSharded)
							{
								_logger.DebugFormat("ShardKeyEnsure for MongoRepo<{0}> ({1})", typeof(T).Name, ts.Name);
								EnableSharding();
							}
							else
								_logger.DebugFormat("ShardKeyEnsure: Connection is not ShardRouter, fake ensure for {0}.", typeof(T).Name);

							Collection.CreateIndex(IndexKeys.Ascending("_sh"), IndexOptions.SetBackground(true));
							if (isSharded)
								ShardCollection("_sh");
						}
						else if (isSharded && (enter = MatchShardRegEx(t, SHARDED_BY_ID_RE, out ts)))
						{
							_logger.DebugFormat("ShardKeyEnsure for MongoRepo<{0}> ({1})", typeof(T).Name, ts.Name);
							EnableSharding();
							ShardCollection("_id");
						}	
                    }
					catch (Exception)
					{
						_logger.ErrorFormat("ShardKeyEnsure for {0}#{1} -> {2}", _dbName, _cfName, _cfg.ToJson());
						throw;
					}
                    finally
                    {
						//if (enter)
						//	timer.Log(_logger.Info, _logger.Warn, SLOW_INDEX);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorFormat("ShardKeyEnsure:{0} ex:{1}", _cfName, ex.ToString());
                throw;
            }
        }

		internal protected static readonly Regex SHARDED_BY_SH_RE = new Regex(@".ISharded`1\[(.+)\]$", RegexOptions.Compiled);
		internal protected static readonly Regex SHARDED_BY_ID_RE = new Regex(@".IShardedById`1\[(.+)\]$", RegexOptions.Compiled);
        bool MatchShardRegEx(Type t, Regex re, out Type baseIdType)
        {
            bool ok = false;
			var found = (from i in t.GetInterfaces()
						 let m = re.Match(i.ToString())
						 where m.Success && m.Groups.Count > 1
						 select new
						 {
							 Child = Type.GetType(m.Groups[1].Value),
							 Main = i,
						 }).FirstOrDefault();
			if (ok = (found != null))
				baseIdType = found.Main;
			else
				baseIdType = null;
            return ok;
        }

		const string ADMIN_DB = "admin";
        void EnableSharding()
        {
            var cmd = new CommandDocument("enablesharding", Database.Name);
            try
            {
				MongoDatabaseSettings dbCfg = Server.Settings.ExtractDatabaseSettings();
                var adminDb = new MongoDatabase(Server, ADMIN_DB, dbCfg);
                Server.Connect();
                adminDb.RunCommand(cmd);
            }
            catch (MongoCommandException mex)
            {
                if (!mex.Message.ToLower().Contains("already enabled"))
                    _logger.Warn(cmd.ToJson(), mex);
            }
            catch (Exception ex)
            {
                _logger.Error(cmd.ToJson(), ex);
                throw;
            }
        }
        void ShardCollection(string shKeyName)
        {
            var cmd = new CommandDocument
            {
                { "shardCollection", Database.Name + '.' + Collection.Name },
                { "key", new BsonDocument(shKeyName, 1) },
            };
            try
            {
				MongoDatabaseSettings dbCfg = Server.Settings.ExtractDatabaseSettings();
                var adminDb = new MongoDatabase(Server, ADMIN_DB, dbCfg);
                Server.Connect();
                CommandResult shcr = adminDb.RunCommand(cmd);
            }
            catch (MongoCommandException mex)
            {
                if (!mex.Message.ToLower().Contains("already "))
                    _logger.Warn(cmd.ToJson(), mex);
            }
            catch (Exception ex)
            {
                _logger.Error(cmd.ToJson(), ex);
                throw;
            }
        }

        /// <summary>
        /// Pulls the overridden collection name passed in the URL after a # symbol, and uses it instead of the Type name.
        /// </summary>
        /// <param name="original"></param>
        /// <returns></returns>
        static Tuple<string, string> ExtractCollectionOverride(string original)
        {
            if (original == null)
                throw new ArgumentNullException();

            string u = original;
            int ix = u.LastIndexOf('#');
            if (ix > 0)
                return Tuple.Create(u.Remove(ix), u.Substring(ix + 1));
            else
                return Tuple.Create(original, string.Empty);
        }

        #endregion

    }
}
