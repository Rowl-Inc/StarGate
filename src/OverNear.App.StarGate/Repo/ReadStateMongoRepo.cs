using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

using log4net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	[Serializable]
	public class ReadStateMongoRepo : ReadStateRepo
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		[XmlIgnore]
		string _path = "mongodb://localhost/StarGate";
		[XmlAttribute]
		public override string Path
		{
			get { return _path; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("MongoPath can not be null or blank");

				if (!value.StartsWith("mongodb://"))
					throw new InvalidOperationException("MongoPath does not start with mongodb://");

				_path = value;
			}
		}

		const int DEFAULT_POOLSIZE = 8;
		[XmlIgnore]
		int _pool = DEFAULT_POOLSIZE;
		[XmlAttribute]
		public int MaxPoolSize
		{
			get { return _pool; }
			set
			{
				if (value > 20)
					_pool = 20;
				else if (value < DEFAULT_POOLSIZE)
					_pool = DEFAULT_POOLSIZE;
				else
					_pool = value;
			}
		}

		[XmlIgnore]
		MongoRepo<ReadState> _repo;
		[XmlIgnore]
		readonly object _rlock = new object();
		[XmlIgnore]
		MongoRepo<ReadState> Repo
		{
			get
			{
				lock (_rlock)
				{
					if (_repo == null)
					{
						var ub = new MongoUrlBuilder(Path);
						ub.MaxConnectionIdleTime = TimeSpan.FromSeconds(20);
						ub.MaxConnectionLifeTime = TimeSpan.FromMinutes(5);
						ub.MaxConnectionPoolSize = MaxPoolSize;
						ub.MinConnectionPoolSize = MaxPoolSize / 2;
						
						if (string.IsNullOrWhiteSpace(ub.DatabaseName))
							ub.DatabaseName = "StarGate";

						MongoUrl mongoPath = ub.ToMongoUrl();
						_repo = new MongoRepo<ReadState>(mongoPath);
					}
					return _repo;
				}
			}
		}

		public override ReadState Load(string id)
		{
			return Load(new[] { id }).FirstOrDefault();
		}

		public override ICollection<ReadState> Load(IEnumerable<string> ids)
		{
			try
			{
				var nh = new HashSet<string>();
				if (ids != null)
				{
					(from n in ids
					 where !string.IsNullOrWhiteSpace(n)
					 select n.ToLower().Trim()).ForEach(n => nh.Add(n));
				}

				IMongoQuery q = Query<ReadState>.In(o => o.Id, nh);
				MongoCursor<ReadState> states = Repo.Collection.Find(q)
					.SetReadPreference(ReadPreference.PrimaryPreferred);

				return states.ToArray();
			}
			//catch (MongoQueryException qex)
			//{
			//	_logger.Error("Load", qex);
			//	Repo.Server.Reconnect();
			//	Repo.Server.VerifyState();
			//	throw;
			//}
			catch (Exception ex)
			{
				var sb = new StringBuilder("Load: ");
				sb.AppendItems(ids);
				_logger.Error(sb.ToString(), ex);
				throw;
			}
		}

		public override bool Create(ReadState state)
		{
			if (state == null)
				throw new ArgumentNullException("state");

			try
			{
				WriteConcernResult rc = Repo.Collection.Insert(state, new MongoInsertOptions
				{
					WriteConcern = WriteConcern.Acknowledged,
					Flags = InsertFlags.ContinueOnError, //suppress error...
				});
#if DEBUG
				if (rc.HasLastErrorMessage)
					Console.WriteLine("Create(...) LastErrorMessage: {0}", rc.LastErrorMessage);
#endif
				return rc.Ok && !rc.HasLastErrorMessage && !rc.UpdatedExisting;
			}
			catch (WriteConcernException wcex)
			{
				_logger.Warn(string.Format("Create({0}) fails. _id already exists! {1}", state, wcex.Message));
#if DEBUG
				Console.WriteLine(wcex);
#endif
				return false;
			}
		}

		public override bool UpdateTimeStamp(string id, BsonTimestamp newTimeStamp, BsonTimestamp lastTimeStamp = null)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");
			if (newTimeStamp == null)
				throw new ArgumentNullException("newTimeStamp");
			if (lastTimeStamp != null && lastTimeStamp > newTimeStamp)
				throw new ArgumentOutOfRangeException("lastTimeStamp can not be larger than newTimeStamp");

			id = id.Trim().ToLower();
			var ands = new List<IMongoQuery> { Query<ReadState>.EQ(o => o.Id, id) };
			if (lastTimeStamp == null || lastTimeStamp.Value == 0)
				ands.Add(Query<ReadState>.LT(o => o.TimeStamp, newTimeStamp));
			else
				ands.Add(Query<ReadState>.EQ(o => o.TimeStamp, lastTimeStamp));

			IMongoQuery q = Query.And(ands);
			IMongoUpdate u = Update<ReadState>.Set(o => o.TimeStamp, newTimeStamp);
			var famas = new FindAndModifyArgs
			{
				Query = Query.And(ands),
				Update = Update<ReadState>.Set(o => o.TimeStamp, newTimeStamp),
				SortBy = SortBy.Null,
				Fields = Fields.Include("_id"),
				VersionReturned = FindAndModifyDocumentVersion.Original,
				Upsert = false,
			};
			FindAndModifyResult fr = Repo.Collection.FindAndModify(famas);
			return fr.Ok && fr.ModifiedDocument != null && fr.ModifiedDocument["_id"].AsString == id;
		}

		public override void Clear(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");

			IMongoQuery q = Query<ReadState>.EQ(o => o.Id, id.Trim().ToLower());
			Repo.Collection.Remove(q, RemoveFlags.Single, WriteConcern.Acknowledged);
		}

		public override void ClearAll()
		{
			Repo.Collection.RemoveAll(WriteConcern.Unacknowledged);
		}
	}
}
