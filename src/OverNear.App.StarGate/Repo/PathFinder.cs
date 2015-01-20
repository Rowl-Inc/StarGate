using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using log4net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;

using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	/// <summary>
	/// Find and check replica-set
	/// </summary>
	public class PathFinder
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		const string CONFIG_DB = "config";
		const string LOCAL_DB = "local";
		const string OP_TABLE = "oplog.rs";
		const string SH_TABLE = "shards";

		readonly List<BsonDocument> _shards = new List<BsonDocument>();
		readonly bool _isReplSet = false;
		readonly MongoUrl _originalPath;

		public VerboseLogLevel VerboseLog { get; set; }

		public static bool IsReplicaSet(MongoServer server)
		{
			if (server == null)
				throw new ArgumentNullException("server");

			bool isRepleSet = false;
			try
			{
				MongoDatabase dbHandle = server.GetDatabase(LOCAL_DB);
				if (dbHandle != null)
					isRepleSet = dbHandle.CollectionExists(OP_TABLE); //replica-set
			}
			catch (MongoException mex)
			{
				_logger.Warn("IsReplicaSet check failed: " + mex.Message);
			}
			return isRepleSet;
		}

		public PathFinder(string mongoPath = null)
		{
			if (string.IsNullOrWhiteSpace(mongoPath))
				mongoPath = "mongodb://localhost/";

			_originalPath = new MongoUrl(mongoPath);

			MongoUrl mu = MongoUrl.Create(mongoPath.ToString());
			MongoServerSettings sett = MongoServerSettings.FromUrl(mu);
			sett.ConnectionMode = ConnectionMode.Automatic;
			sett.ReadPreference = ReadPreference.SecondaryPreferred;

			var server = new MongoServer(sett);

			server.Connect(TimeSpan.FromSeconds(5)); //don't wait too long
			switch (server.State)
			{
				case MongoServerState.Connected:
				case MongoServerState.ConnectedToSubset:
					break;
				default:
					throw new MongoException("Could not connect to MongoServer: " + mongoPath);
			}
			if (_logger.IsDebugEnabled)
			{
				var sb = new StringBuilder("Checking ");
				server.Instances.ForEach(s => sb.AppendFormat("{0},", s.Address));
				sb.Length--;
				sb.Append('/');
				sb.Append(CONFIG_DB);
				_logger.Debug(sb);
			}

			//force cleanup
			foreach (MongoServerInstance node in server.Instances)
			{
				var rp = new MongoRepo(_originalPath);
				{
					if (IsFrozenLocked(rp))
						UnFreezeUnLock(rp);
				}
			}

			try
			{
				MongoDatabase cfgdb = server.GetDatabase(CONFIG_DB);
				if (cfgdb != null && cfgdb.CollectionExists(SH_TABLE))
				{
					MongoCollection ops = cfgdb.GetCollection(SH_TABLE);
					MongoCursor cursor = ops.FindAllAs<BsonDocument>().SetReadPreference(ReadPreference.PrimaryPreferred);
					foreach (BsonDocument d in cursor)
					{
						_shards.Add(d);
					}
				}
			}
			catch (MongoException mex)
			{
				_logger.Warn("Shard check failed: " + mex.Message);
			}

			if (_shards.IsNullOrEmpty()) //replica-set
				_isReplSet = IsReplicaSet(server);

			if (_shards.IsNullOrEmpty() && !_isReplSet)
				throw new MongoException("Unable to find collection: " + OP_TABLE);
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

		internal bool UnFreezeUnLock(MongoRepo rp)
		{
			if (rp == null)
				throw new ArgumentNullException("rp");

			try
			{
				bool unFreezeOk = false, unLockOk = false;
				//MongoCollection<BsonDocument> mc = rp.AdminDb.GetSisterDatabase("admin").GetCollection<BsonDocument>("$cmd.sys.unlock");
				MongoCollection<BsonDocument> mc = rp.AdminDb.GetCollection<BsonDocument>("$cmd.sys.unlock");
				BsonDocument r = mc.FindOne();

				//BsonValue v = rp.AdminDb.Eval("db.fsyncUnlock();");
				//BsonDocument r = v != null && v.IsBsonDocument ? v.AsBsonDocument : null;

				unLockOk = r != null && r.Contains("ok") && r["ok"].ToString() == "1";
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.DebugFormat("UnFreezeUnLock: $cmd.sys.unlock -> {0}", r);

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

		public static MongoUrl GetFullReplSetUrl(MongoUrl u)
		{
			if (u == null)
				throw new ArgumentNullException("u");

			var server = new MongoServer(MongoServerSettings.FromUrl(u));
			server.Connect();
			if (server.DbExists(LOCAL_DB)) //replica set
			{
				CommandResult cr = server.GetDatabase("admin").RunCommand("replSetGetStatus");
				if (cr != null && cr.Response != null && cr.Response.Contains("members"))
				{
					string[] names = (from v in cr.Response["members"].AsBsonArray
									  where v.IsBsonDocument
									  let n = v.AsBsonDocument
									  where n != null && n.Contains("name")
									  let name = n["name"].AsString
									  orderby name ascending
									  select name).ToArray();
					if (!names.IsNullOrEmpty())
					{
						var ub = new MongoUrlBuilder(u.ToString());
						ub.Servers = (from n in names
									  let arr = n.Split(':')
									  let port = int.Parse(arr.Last())
									  select new MongoServerAddress(arr.First(), port)).ToArray();
						u = new MongoUrl(ub.ToMongoUrl().ToString().ToLower()); //normalized
					}
				}
			}
			return u;
		}

		public MongoUrl Match(string exact, bool ignoreCase = false, FinderMatchPart parts = FinderMatchPart.All)
		{
			return Match(new Regex(exact ?? string.Empty, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), parts);
		}

		const string SH_HOST = "host";
		const string SH_ID = "_id";
		static readonly Regex SH_RM_RE = new Regex(@"^[^\/]+\/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		public MongoUrl Match(Regex re, FinderMatchPart parts = FinderMatchPart.All)
		{
			if (_isReplSet)
				return _originalPath;

			MongoUrl res = null;
			foreach (BsonDocument d in _shards)
			{
				bool isMatch = false;
				if (!isMatch && d.Contains(SH_ID) && (parts & FinderMatchPart.ShardId) == FinderMatchPart.ShardId)
					isMatch = re.IsMatch(d[SH_ID].AsString);

				if (!isMatch && d.Contains(SH_HOST) &&(parts & FinderMatchPart.Host) == FinderMatchPart.Host)
					isMatch = re.IsMatch(d[SH_HOST].AsString);

				if (isMatch)
				{
					res = ExtractUrl(d);
					break;
				}
			}
			return res;
		}

		static MongoUrl ExtractUrl(BsonDocument d)
		{
			string s = d[SH_HOST].AsString;
			if (SH_RM_RE.IsMatch(s))
				s = SH_RM_RE.Replace(s, string.Empty); //cleanup

			if (s.Last() == '/')
				s = s.Remove(s.Length - 1);

			return new MongoUrl("mongodb://" + s + '/' + LOCAL_DB);
		}

		public IList<MongoUrl> AllShards()
		{
			var shards = new List<MongoUrl>();
			foreach (BsonDocument d in _shards)
			{
				shards.Add(ExtractUrl(d));
			}
			return shards;
		}

	}
}
