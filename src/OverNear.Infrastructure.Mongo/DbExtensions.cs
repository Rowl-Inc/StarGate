using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Configuration;
using System.Threading;
using System.Web;

using log4net;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoDB.Driver.Builders;

using OverNear.Infrastructure;

namespace OverNear.Infrastructure
{
    public static class DbExtensions
    {
		static readonly ILog _logger = LogManager.GetLogger(typeof(Extensions));

        public static ReadPreference Configured(this ReadPreference rp)
		{
			ReadPreference r = rp;
			//ForceReadPreference fm = SystemSettings.Instance.ForceReadPreference;
			//if (fm != ForceReadPreference.NotSet)
			//{
			//	ReadPreferenceMode m = (ReadPreferenceMode)(((int)fm) - 1);
			//	var n = new ReadPreference(m);
			//	if (GetStrictWeight(r.ReadPreferenceMode) < GetStrictWeight(n.ReadPreferenceMode))
			//		r = n;
			//}
			return r;
		}

		public static DateTime ToDateTime(this BsonTimestamp ts)
		{
			if (ts == null)
				return DateTime.MinValue;

			DateTime dt = ((long)ts.Timestamp).FromUnixTime();
			return (dt - TimeSpan.FromMilliseconds(dt.Millisecond)) + TimeSpan.FromMilliseconds(ts.Increment);
		}

		public static BsonTimestamp ToTimestamp(this DateTime dt)
		{
			int epox = (int)dt.ToUnixTime();
			int hash = dt.Millisecond;
			return new BsonTimestamp(epox, hash);
		}

		public static MongoCollectionSettings ExtractCollectionSettings(this MongoDatabaseSettings dbs)
		{
			return new MongoCollectionSettings
			{
				ReadEncoding = dbs.ReadEncoding,
				WriteEncoding = dbs.WriteEncoding,
				ReadPreference = dbs.ReadPreference,
				WriteConcern = dbs.WriteConcern,
				GuidRepresentation = dbs.GuidRepresentation,
			};
		}

		public static MongoDatabaseSettings ExtractDatabaseSettings(this MongoServerSettings ss)
		{
			return new MongoDatabaseSettings
			{
				GuidRepresentation = ss.GuidRepresentation,
				ReadEncoding = ss.ReadEncoding,
				WriteEncoding = ss.WriteEncoding,
				ReadPreference = ss.ReadPreference,
				WriteConcern = ss.WriteConcern,
			};
		}

		public static Exception TryConnect(this MongoServer self, bool doNotVerify = false)
		{
			if (self == null)
				return new ArgumentNullException("self");

			try
			{
				switch (self.State)
				{
					case MongoServerState.Disconnected:
					case MongoServerState.Disconnecting:
						self.Connect();
						if(!doNotVerify)
							self.VerifyState();
						break;
				}
				return null;
			}
			catch (Exception ex)
			{
				return ex;
			}
		}

		public static DateTime ToUTC(this BsonTimestamp ts)
		{
			if (ts == null)
				throw new ArgumentNullException("ts");

			return ts.Timestamp.FromUnixTime().AddTicks(ts.Increment);
		}

		public static bool DbExists(this MongoServer svr, string dbName)
		{
			bool exists = false;
			try
			{
				if (svr != null && !string.IsNullOrWhiteSpace(dbName))
				{
					MongoDatabase db = svr.GetDatabase(dbName);
					if (db != null)
					{
						DatabaseStatsResult sr = db.GetStats();
						exists = sr != null && sr.Ok;
					}
				}
			}
			catch (MongoException mex)
			{
				_logger.WarnFormat("DbExists: {0}", mex, dbName);
			}
			return exists;
		}

		public static MongoUrl ExtractValue(this System.Collections.Specialized.NameValueCollection appSettings, string key, MongoUrl defaultValue)
		{
			if (appSettings.HasKeys())
			{
				string v = appSettings.Get(key);
				if (!string.IsNullOrWhiteSpace(v))
					return new MongoUrl(v);
			}
			return defaultValue;
		}

		/// <summary>
		/// Clone value that is native, object, or array and fix all the .0 decimal to int or long
		/// </summary>
		public static BsonValue FixDouble(this BsonValue o)
		{
			if (o == null)
				return o;

			if (o.IsDouble)
			{
				double og = o.AsDouble;
				double floor = Math.Floor(og);
				if (og == floor)
				{
					if (og <= int.MaxValue)
						return (int)og;
					else
						return (long)og;
				}
				else
					return og;
			}
			else if (o.IsBsonDocument)
			{
				var newDoc = new BsonDocument();
				foreach (BsonElement el in o.AsBsonDocument)
				{
					if (el.Name[0] == '$')
						return o; //quit here...

					BsonValue v = FixDouble(el.Value);
					newDoc.Add(el.Name, v);
				}
				return newDoc;
			}
			else if (o.IsBsonArray)
			{
				var newArr = new BsonArray();
				foreach (BsonValue v in o.AsBsonArray)
				{
					newArr.Add(FixDouble(v));
				}
				return newArr;
			}
			else
				return o;
		}
    }
}
