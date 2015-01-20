using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using System.Web;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoDB.Driver.Builders;

using log4net;

namespace OverNear.Infrastructure
{
    public class MongoRepo : IMongoRepo
    {
        static protected readonly ILog _logger = LogManager.GetLogger(typeof(Extensions));

        readonly MongoServer _server;
        public virtual MongoServer Server { get { return _server; } }

        readonly MongoDatabase _db;
        //Removed virtual descriptor, this can be virtual if we need to override it later, otherwise
        //ReSharper (appropriately) warns that MongoRepo_T calls this virtual method in its constructor, which was not
        //necessarily dangerous as it was, but could have been later down the road.
        public MongoDatabase Database { get { return _db; } }

		static readonly MongoUrl _nullURL = null;

        /// <summary>
        /// Instantiate with appConfigKey that store the mongo connection string value. 
        /// Value should be mongoUrl connection string
        /// NOTE: use #collection_name to override collection in configuration (instead of using object names)
        /// </summary>
        /// <param name="appConfigKey">appConfig entry Key</param>
        /// <param name="serverSettings">lambda function to override parsed settings</param>
        public MongoRepo(string appConfigKey,
            Func<MongoServerSettings, MongoServerSettings> serverSettings = null,
            Func<MongoDatabaseSettings, MongoDatabaseSettings> databaseSettings = null)
			: this(ConfigurationManager.AppSettings.ExtractValue(appConfigKey, _nullURL), serverSettings, databaseSettings)
        {
        }

        protected static readonly Regex VALID_MONGO_NAMES = new Regex(@"[\s\W]", RegexOptions.Compiled);
		internal protected static readonly Regex TEST_ASSEMBLY = new Regex(@"(NUnitLauncher\.exe|nunit\.tdnet\.dll|overnear[\w\d.]+tests?\W|nunit)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		internal protected static readonly Regex ENVIRONMENTS = new Regex(@"_(local|development|test|qa|staging|stage|production|prod)$", RegexOptions.Compiled);

		protected readonly string _dbName;

		public MongoRepo(MongoUrl connectionString,
			Func<MongoServerSettings, MongoServerSettings> serverSettings = null,
			Func<MongoDatabaseSettings, MongoDatabaseSettings> databaseSettings = null,
			Func<string, string> defaultDbName = null)
		{
			try
			{
				if (connectionString == null)
					throw new ArgumentNullException("connectionString");
				if (string.IsNullOrWhiteSpace(connectionString.DatabaseName))
					throw new ArgumentException("connectionString.DatabaseName can not be null or blank");

				connectionString = AppendCommonConnCfg(connectionString);
				string dbName = connectionString.DatabaseName;
				if (!string.IsNullOrWhiteSpace(dbName))
				{
					int ix = dbName.IndexOf('#');
					if (ix > 0)
						dbName = dbName.Remove(ix);
				}
				if (string.IsNullOrWhiteSpace(dbName))
					throw new ArgumentException("connectionString.DatabaseName is required!");

				if (dbName.Last() == '/')
					dbName = dbName.Remove(dbName.Length - 1);

				//if (TEST_ASSEMBLY.IsMatch(AppDomain.CurrentDomain.FriendlyName) && !dbName.ToLower().EndsWith(SystemSettings.Instance.NUnitDbName))
				//{
				//	dbName = ENVIRONMENTS.Replace(dbName, string.Empty);
				//	dbName += SystemSettings.Instance.NUnitDbName;
				//}

				if (defaultDbName != null)
					dbName = defaultDbName(dbName);

				if (VALID_MONGO_NAMES.IsMatch(dbName))
					dbName = VALID_MONGO_NAMES.Replace(dbName, "_");

				_dbName = dbName;
				MongoServerSettings serverCfg = MongoServerSettings.FromUrl(connectionString);
				if (serverSettings != null)
					serverCfg = serverSettings(serverCfg);

				_server = new MongoServer(serverCfg);
				MongoDatabaseSettings dbCfg = Server.Settings.ExtractDatabaseSettings();
				if (databaseSettings != null)
					dbCfg = databaseSettings(dbCfg);

				_db = new MongoDatabase(Server, _dbName, dbCfg);
			}
			catch (Exception ex)
			{
				_logger.Fatal("CTOR: " + this.GetType().FullName, ex);
				throw;
			}
		}

		static readonly Dictionary<string, object> DEFAULT_MONGO_OPTIONS = new Dictionary<string, object>
		{
			{ "connectTimeout", "10s" },
			//{ "socketTimeout", "15s" },
			//{ "maxPoolSize", 40 },
			{ "minPoolSize", 5 },
			//{ "maxIdleTime", "30s" },
			//{ "waitQueueTimeout", "18s" },
			{ "waitQueueMultiple", 1000 },
		};
		/// <summary>
		/// Fetch the common connection string overrides and append them
		/// </summary>
		static MongoUrl AppendCommonConnCfg(MongoUrl path)
		{
			if (path == null)
				return path;

			Dictionary<string, object> keeps = DEFAULT_MONGO_OPTIONS.ToDictionary(o => o.Key, o => o.Value);
			keeps = MergeOptions(keeps, SplitMongoOptions(ConfigurationManager.AppSettings.ExtractConfiguration("DEFAULT_MONGO_OPTIONS", string.Empty)));
			keeps = MergeOptions(keeps, SplitMongoOptions(path));
			return MergeUrl(path, keeps);
		}

		static MongoUrl MergeUrl(MongoUrl original, Dictionary<string, object> replace)
		{
			string p = original.ToString();
			var sb = new StringBuilder(p);
			int ix = p.IndexOf('?');
			if (ix > 0)
				sb.Length = ix;

			if (!replace.IsNullOrEmpty())
			{
				sb.Append('?');
				foreach (KeyValuePair<string, object> o in replace)
				{
					sb.Append(HttpUtility.UrlEncode(o.Key));
					sb.Append('=');
					if (o.Value != null)
						sb.Append(HttpUtility.UrlEncode(o.Value.ToString()));
					sb.Append(';');
				}
				sb.Length--;
			}
			return new MongoUrl(sb.ToString());
		}
		static Dictionary<string, object> MergeOptions(Dictionary<string, object> keep, Dictionary<string, object> optional)
		{
			if (keep.IsNullOrEmpty() || optional.IsNullOrEmpty())
				return keep;

			foreach (KeyValuePair<string, object> o in optional)
			{
				if (keep.ContainsKey(o.Key))
					keep[o.Key] = o.Value;
				else
					keep.Add(o.Key, o.Value);
			}
			return keep;
		}
		static Dictionary<string, object> SplitMongoOptions(MongoUrl path)
		{
			string p = path.ToString();
			p = p.Replace("mongodb://", "http://");
			int ix = p.IndexOf('?');
			if (ix > 0)
				return SplitMongoOptions(p.Substring(ix));
			else
				return new Dictionary<string, object>();
		}
		static Dictionary<string, object> SplitMongoOptions(string optionString)
		{
			if (string.IsNullOrWhiteSpace(optionString))
				return new Dictionary<string, object>();
			
			if(optionString.First() == '?')
				optionString = optionString.Substring(1);

			return (from setting in optionString.Split('&', ';')
					let pair = setting.Split('=')
					where pair != null && pair.Length == 2
					let p = new
					{
						k = HttpUtility.UrlDecode(pair.FirstOrDefault().Trim()),
						v = HttpUtility.UrlDecode(pair.LastOrDefault()),
					}
					group p by p.k into pg
					select pg.FirstOrDefault()).ToDictionary(o => o.k, o => o.v as object);
		}

        //public static MongoConstants Constants { get { return MongoConstants.Constants; } }

		readonly object _adminLock = new object();
		MongoDatabase _adminDb = null;
		public virtual MongoDatabase AdminDb
		{
			get
			{
				lock (_adminLock)
				{
					if (_adminDb == null)
						_adminDb = Server.GetDatabase("admin");
					return _adminDb;
				}
			}
		}

    }
}
