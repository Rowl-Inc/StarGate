using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Configuration;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using log4net;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.GridFS;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;
using OverNear.App.StarGate.Repo;

namespace OverNear.App.StarGate
{
	[XmlInclude(typeof(ReadStateRepo))]
	[XmlInclude(typeof(ReadStateElasticRepo))]
	[XmlInclude(typeof(ReadStateElasticAsyncRepo))]
	[XmlInclude(typeof(ReadStateMongoRepo))]
	[XmlRoot("StarGate")]
	[Serializable]
	public class Settings
	{
		static readonly ILog _logger = LogManager.GetLogger(typeof(OverNear.Infrastructure.Extensions));
		static readonly XmlSerializer SERIALIZER;

		#region CTORs & Serializer functions

		static Settings()
		{
			SERIALIZER = new XmlSerializer(typeof(Settings));
		}

		//const string DEFAULT_READSTATE_PATH = "mongodb://localhost/StarGate";

		/// <summary>
		/// Blank settings for serializer compatibility
		/// </summary>
		public Settings() 
		{
			//defaults...
			//IgnoreNameSpace = @"(^(admin|config|local|StarGate)\.|(\.system\.|fs\.chunks|_NUNIT))";
			IgnoreNameSpace = @"(^(admin|config|local|StarGate)\.|(\.system\.|fs\.chunks))";
			//ReadStatePath = DEFAULT_READSTATE_PATH;

			NoMasterSleep = TimeSpan.FromSeconds(30);
			NoDataSleep = TimeSpan.FromSeconds(10);
			CursorRestartSleep = TimeSpan.FromSeconds(1);
		}
		/// <summary>
		/// Override with settings in provided section if applicable
		/// </summary>
		/// <param name="section"></param>
		public Settings(XmlNode section) : this()
		{
			try
			{
				if (section != null && section.HasChildNodes)
				{
					object o = null;
					using (var ms = new MemoryStream())
					using (var sr = new StreamWriter(ms))
					{
						sr.Write(section.OuterXml);
						sr.Flush();
						ms.Position = 0;

						using (XmlReader reader = XmlReader.Create(ms, Constants.SETTINGS_R))
							o = SERIALIZER.Deserialize(reader);
					}
					if (o != null && o is Settings)
					{
						Settings sett = o as Settings; //reference copy
						Routes = sett.Routes;
						ReadThreads = sett.ReadThreads;

						ReplicaName = sett.ReplicaName;
						IgnoreNameSpace = sett.IgnoreNameSpace;
						//ReadStatePath = sett.ReadStatePath;
						this.ReadStateRepo = sett.ReadStateRepo;
						this.BasePathSettings = sett.BasePathSettings;

						NoMasterSleepMs = sett.NoMasterSleepMs;
						NoDataSleepMs = sett.NoDataSleepMs;
						CursorRestartSleepMs = sett.CursorRestartSleepMs;
					}
				}
			}
			catch (Exception ex)
			{
				_logger.Fatal("CTOR: " + (section == null ? string.Empty : section.OuterXml), ex);
				throw;
			}
		}

		static readonly TimeSpan SETTINGS_CACHE = ConfigurationManager.AppSettings.ExtractConfiguration("SETTINGS_CACHE", TimeSpan.FromMinutes(5)); //cache the settings for 1 minute
		static readonly object _slock = new object();

		static Settings _cachedSettings;

		static DateTime _lastFetchedFromConfig = DateTime.MinValue;
		/// <summary>
		/// Pull full config file from app.config StarGate section.
		/// This operation cache using the duration in SETTINGS_CACHE.
		/// If Routes or ReadThreads setting is missing, config will attempt to pull from GridFs
		/// </summary>
		/// <returns>Serialized StarGate config in strongly typed values</returns>
		public static Settings ReadFromAppConfig()
		{
			try
			{
				lock (_slock)
				{
					if (_cachedSettings != null && (DateTime.UtcNow - _lastFetchedFromConfig) < SETTINGS_CACHE)
						return _cachedSettings;

					ConfigurationManager.RefreshSection("StarGate");
					object o = ConfigurationManager.GetSection("StarGate");
					if (o != null && o is Settings)
						_cachedSettings = o as Settings;

					if (_cachedSettings == null)
						throw new ConfigurationErrorsException("Can not load StarGate configration section");
					//else
					//{
					//	_lastFetchedFromConfig = DateTime.UtcNow;
					//	if (!_cachedSettings.Routes.IsNullOrEmpty() && !_cachedSettings.ReadThreads.IsNullOrEmpty() &&
					//		(string.IsNullOrWhiteSpace(_cachedSettings.ReadStatePath) || _cachedSettings.ReadStatePath == DEFAULT_READSTATE_PATH))
					//	{
					//		_logger.Info("ReadFromConfig successfully loaded StarGate config section");
					//		return _cachedSettings;
					//	}
					//}
				}
				//_logger.Info("ReadFromConfig missing Routes or ReadThreads, will retry from GridFs");
				//return ReadFromGridFs(_cachedSettings.ReadStatePath);
				return _cachedSettings;
			}
			catch (Exception ex)
			{
				_logger.Error("ReadFromConfig", ex);
				throw;
			}
		}

		public static Settings ReadFromFile(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentException("filePath can not be null or blank");

			var f = new FileInfo(filePath);
			if (!f.Exists)
				throw new FileNotFoundException(filePath);

			lock (_slock)
			{
				if (_cachedSettings != null && (DateTime.UtcNow - _lastFetchedFromGridFs) < SETTINGS_CACHE)
					return _cachedSettings;
				else
				{
					var xml = new XmlDocument();
					using (FileStream fs = f.OpenRead())
					using (var sr = new StreamReader(fs))
					{
						xml.Load(fs);
						fs.Flush();
					}
					_cachedSettings = new Settings(xml);
					//_cachedSettings.ReadStatePath = f.FullName;
					_lastFetchedFromGridFs = DateTime.UtcNow;
					_logger.InfoFormat("ReadFromFile successfully loaded {0}", f.FullName);

					return _cachedSettings;
				}
			}
		}
		
		const string GFS_CFG_FILE = "stargate_settings.config";
		static DateTime _lastFetchedFromGridFs = DateTime.MinValue;
		/// <summary>
		/// Pull full config file from app.config StarGate section.
		/// This operation cache using the duration in SETTINGS_CACHE
		/// </summary>
		/// <param name="mongoPath">optional mongo path, if missing, ReadStatePath is used</param>
		/// <returns>Serialized StarGate config in strongly typed values</returns>
		public static Settings ReadFromGridFs(string mongoPath = null)
		{
			try
			{
				//if (string.IsNullOrWhiteSpace(mongoPath))
				//{
				//	Settings s = ReadFromAppConfig();
				//	//mongoPath = s.ReadStatePath;
				//}
				if (string.IsNullOrWhiteSpace(mongoPath))
					throw new ConfigurationErrorsException("Configuration property StarGate.ReadStatePath can not be null or empty when mongoPath is null or empty on input");

				string originalPath = mongoPath;
				lock (_slock)
				{
					if (_cachedSettings != null && (DateTime.UtcNow - _lastFetchedFromGridFs) < SETTINGS_CACHE)
						return _cachedSettings;

					//pull from gridfs
					string fn = Path.GetFileName(mongoPath);
					if (string.IsNullOrWhiteSpace(fn))
						fn = GFS_CFG_FILE; //default file name
					else //split the path off
					{
						int ix = mongoPath.LastIndexOf(fn);
						mongoPath = mongoPath.Remove(ix);
					}
					if (mongoPath.Last() == '/')
						mongoPath = mongoPath.Remove(mongoPath.Length - 1);

					MongoUrl url = new MongoUrl(mongoPath);
					MongoServerSettings ms = MongoServerSettings.FromUrl(url);
					ms.ConnectTimeout = TimeSpan.FromSeconds(5);
					ms.SocketTimeout = TimeSpan.FromSeconds(6);
					ms.MaxConnectionPoolSize = 2;
					ms.MinConnectionPoolSize = 1;
					ms.MaxConnectionLifeTime = TimeSpan.FromMinutes(1);
					ms.MaxConnectionIdleTime = TimeSpan.FromSeconds(20);
					ms.WaitQueueSize = 100;
					ms.WaitQueueTimeout = TimeSpan.FromSeconds(5);

					MongoServer mx = new MongoServer(ms);
					MongoDatabase db = string.IsNullOrWhiteSpace(url.DatabaseName) ? mx.GetDatabase("StarGate") : mx.GetDatabase(url.DatabaseName);
					MongoCursor<MongoGridFSFileInfo> cursor = db.GridFS.Find(fn)
						.SetReadPreference(ReadPreference.PrimaryPreferred.Configured())
						.SetSortOrder(SortBy.Descending("filename"))
						.SetLimit(1);

					MongoGridFSFileInfo f = cursor.FirstOrDefault();
					if (f == null)
						throw new FileNotFoundException("StarGate " + fn + " is not found at: " + mongoPath);

					var xml = new XmlDocument();
					using (MongoGridFSStream gfss = f.OpenRead())
					{
						xml.Load(gfss);
						gfss.Flush();
					}
					_cachedSettings = new Settings(xml);
					//_cachedSettings.ReadStatePath = originalPath; //swap the path for this value so next time it is loading correctly, instead of boucing off 

					if (_cachedSettings == null)
						throw new ConfigurationErrorsException("Can not load StarGate configration section");
					else
					{
						_lastFetchedFromGridFs = DateTime.UtcNow;
						_logger.InfoFormat("ReadFromGridFs successfully loaded {0} from {1}", fn, mongoPath);
					}

					return _cachedSettings;
				}
			}
			catch (Exception ex)
			{
				_logger.Error("ReadFromGridFs: " + (mongoPath ?? "<null>"), ex);
				throw;
			}
		}

		#endregion

		//string _readStatePath = DEFAULT_READSTATE_PATH;

		///// <summary>
		///// Where to store StarGate state.
		///// By default (if settings are not provided) data are stored at: mongodb://localhost/
		///// </summary>
		//[XmlAttribute]
		//public string ReadStatePath { get; set; }

		[XmlIgnore]
		string _name;
		/// <summary>
		/// The unique name shared among the same replica set, could be anything,
		/// as long as the same value is used for all nodes in replica set
		/// </summary>
		[XmlAttribute]
		public string ReplicaName
		{
			get { return _name; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Name can not be null or blank");

				_name = value;
			}
		}

		[XmlIgnore]
		readonly object _rlock = new object();
		[XmlIgnore]
		ReadStateRepo _repo;
		/// <summary>
		/// How the readstate is handled...
		/// </summary>
		[XmlElement]
		public ReadStateRepo ReadStateRepo
		{
			get 
			{
				lock (_rlock)
				{
					if (_repo == null)
						_repo = new ReadStateElasticAsyncRepo();

					return _repo;
				}
			}
			set { _repo = value; }
		}

		[XmlElement]
		public BasePathSettings BasePathSettings { get; set; }

		/// <summary>
		/// Regexp of name spaces to ignore. by default, all system db and collections are ignored
		/// </summary>
		[XmlAttribute]
		public string IgnoreNameSpace { get; set; }

		/// <summary>
		/// How long to sleep for before retrying the oplog logic again when masterOnly option is set to true 
		/// and current read thread is not master. Default should be 30s
		/// </summary>
		[XmlAttribute]
		public long NoMasterSleepMs { get; set; }
		[XmlIgnore]
		public TimeSpan NoMasterSleep
		{
			get { return TimeSpan.FromMilliseconds(NoMasterSleepMs); }
			set { NoMasterSleepMs = (long)value.TotalMilliseconds; }
		}

		/// <summary>
		/// How long to slumber if no data is found in the oplog before retrying again.
		/// Default is about 5s.
		/// </summary>
		[XmlAttribute]
		public long NoDataSleepMs { get; set; }
		[XmlIgnore]
		public TimeSpan NoDataSleep
		{
			get { return TimeSpan.FromMilliseconds(NoDataSleepMs); }
			set { NoDataSleepMs = (long)value.TotalMilliseconds; }
		}

		/// <summary>
		/// Similar to NoDataSleep, sometimes the read cursor of the oplog is forced to restart
		/// due to connnection timeout settings, how long to slumber before a reconnect is allowed.
		/// Default is about 500ms
		/// </summary>
		[XmlAttribute]
		public long CursorRestartSleepMs { get; set; }
		[XmlIgnore]
		public TimeSpan CursorRestartSleep
		{
			get { return TimeSpan.FromMilliseconds(CursorRestartSleepMs); }
			set { CursorRestartSleepMs = (long)value.TotalMilliseconds; }
		}

		[XmlIgnore]
		RouteList _routes = new RouteList();
		/// <summary>
		/// Describes how each oplog entry should be routed to what logic or destination
		/// </summary>
		[XmlArray]
		public RouteList Routes
		{
			get { return _routes; }
			set
			{
				if (value == null)
					_routes.Clear();
				else
					_routes = value;
			}
		}

		[XmlIgnore]
		ReadThreadList _readThreads = new ReadThreadList();
		/// <summary>
		/// Describes how many concurrent threads should be allocated for the configured routes
		/// </summary>
		[XmlArray]
		public ReadThreadList ReadThreads
		{
			get { return _readThreads; }
			set
			{
				if (value == null)
					_readThreads.Clear();
				else
					_readThreads = value;
			}
		}

		public override string ToString()
		{
			var sb = new StringBuilder();
			using (XmlWriter writer = XmlWriter.Create(sb, Constants.SETTINGS_W))
			{
				SERIALIZER.Serialize(writer, this);
			}
			return sb.ToString();
		}
	}
}
