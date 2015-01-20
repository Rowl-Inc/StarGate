using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Subscribe
{
	public class OpLogContext : IContext
	{
		[JsonIgnore]
		readonly IMongoRepo _repo;

		[JsonIgnore]
		IResponsibilityChain _tasks;

		public OpLogContext(OpLogLine original, IMongoRepo repo, string configuredDb, IResponsibilityChain taskChain = null)
		{
			if (original == null)
				throw new ArgumentNullException("original");
			if (repo == null)
				throw new ArgumentNullException("repo");

			Original = original;
			_repo = repo;
			_tasks = taskChain;
			ConfiguredDb = configuredDb;

			string json = original.ToJson(Constants.Instance.STRICT_JSON);
			Payload = BsonDocument.Parse(json);
		}

		public OpLogLine Original { get; private set; }
		public BsonValue Payload { get; set; }
		public VerboseLogLevel VerboseLog { get; set; }

		[JsonIgnore]
		public IResponsibilityChain TaskChain { get { return _tasks; } }

		[JsonIgnore]
		public IMongoRepo CurrentRepo { get { return _repo; } }

		public override string ToString()
		{
			if (Original != null)
			{
				//if (Source.Payload != null)
				//	return Source.Payload.ToJson();
				//else
					return Original.ToJson();
			}
			else
				return this.ToJson();
		}

		public string ConfiguredDb { get; private set; }

		public virtual object Clone()
		{
			var c = new OpLogContext(Original, _repo, ConfiguredDb, _tasks);
			c.Payload = Payload;
			c.VerboseLog = VerboseLog;
			return c;
		}

		public virtual IContext Copy()
		{
			return Clone() as IContext;
		}
	}
}
