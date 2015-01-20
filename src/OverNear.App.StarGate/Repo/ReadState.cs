using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	[BsonIgnoreExtraElements]
	public class ReadState : IShardedById<string>
	{
		internal ReadState(RS rs)
		{
			if (rs != null)
			{
				Id = rs.Id;
				if (rs.TimeStamp != null)
					TimeStamp = new BsonTimestamp(rs.TimeStamp.Epoch, rs.TimeStamp.Increment);
			}
		}
		public ReadState() { }

		[JsonIgnore]
		[BsonIgnore]
		string _id;
		/// <summary>
		/// Who
		/// </summary>
		[JsonProperty("_id")]
		[BsonId, BsonRequired]
		public string Id
		{
			get { return _id; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Id can not be null or blank!");

				_id = value.Trim().ToLower();
			}
		}

		[JsonIgnore]
		[BsonIgnore]
		BsonTimestamp _timeStamp = new BsonTimestamp(0);
		/// <summary>
		/// When
		/// </summary>
		[JsonProperty("ts")]
		[BsonElement("ts"), BsonRequired]
		public BsonTimestamp TimeStamp
		{
			get { return _timeStamp; }
			set { _timeStamp = value ?? new BsonTimestamp(0); }
		}

		internal RS ToRs()
		{
			return new RS
			{
				Id = this.Id,
				TimeStamp = new TS
				{
					Epoch = TimeStamp.Timestamp,
					Increment = TimeStamp.Increment,
				}
			};
		}
	}

	internal class RS
	{
		[JsonProperty("_id")]
		public string Id { get; set; }
		[JsonProperty("ts")]
		public TS TimeStamp { get; set; }
	}

	internal class TS
	{
		[JsonProperty("epx")]
		public int Epoch { get; set; }
		[JsonProperty("inc")]
		public int Increment { get; set; }
	}
}
