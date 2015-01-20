using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;

using OverNear.Infrastructure;


using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Attributes;

namespace OverNear.App.StarGate
{
	/// <summary>
	/// <see cref="https://github.com/krickert/JavaMongoDBOpLogReader/blob/master/src/main/java/com/krickert/mongodb/oplog/OplogLine.java"/>
	/// </summary>
	[BsonIgnoreExtraElements]
	public class OpLogLine
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		const string NO_OP = "!";

		[BsonIgnore]
		readonly object _oplock = new object(); //thread safety & atomicity

		[BsonIgnore]
		string _op = NO_OP;
		[BsonElement("op")]
		public virtual string Op
		{
			get { lock(_oplock) return _op; }
			set
			{
				lock (_oplock)
				{
					_op = string.IsNullOrWhiteSpace(value) ? NO_OP : value;
					_opType = ParseType(_op);
				}
			}
		}
		internal static OpLogType ParseType(string op)
		{
			OpLogType t;
			switch (op ?? string.Empty)
			{
				case "n":
					t = OpLogType.NoOp;
					break;
				case "i":
					t = OpLogType.Insert;
					break;
				case "u":
					t = OpLogType.Update;
					break;
				case "d":
					t = OpLogType.Delete;
					break;
				case "c":
					t = OpLogType.Command;
					break;
				case NO_OP:
				default:
					t = OpLogType.Unknown;
					break;
			}
			return t;
		}

		[BsonIgnore]
		OpLogType _opType = OpLogType.Unknown;
		[BsonIgnore]
		public virtual OpLogType Operation
		{
			get { lock(_oplock) return _opType; }
			set  { lock (_oplock) { _op = ToString(_opType = value); } }
		}
		internal static string ToString(OpLogType t)
		{
			string s;
			switch (t)
			{
				case OpLogType.NoOp:
					s = "n";
					break;
				case OpLogType.Insert:
					s = "i";
					break;
				case OpLogType.Update:
					s = "u";
					break;
				case OpLogType.Delete:
					s = "d";
					break;
				case OpLogType.Command:
					s = "c";
					break;
				case OpLogType.Unknown:
				default:
					s = NO_OP;
					break;
			}
			return s;
		}

		[BsonIgnore]
		readonly object _dlock = new object();

		[BsonIgnore]
		BsonTimestamp _ts;
		[BsonElement("ts"), BsonRequired]
		public virtual BsonTimestamp TimeStamp
		{
			get { lock(_dlock) return _ts; }
			set { lock (_dlock) { _dt = (_ts = value).ToDateTime(); } }
		}

		[BsonIgnore]
		DateTime _dt;
		[BsonIgnore]
		public virtual DateTime Created
		{
			get { lock(_dlock) return _dt; }
			set { lock (_dlock) { _ts = (_dt = value).ToTimestamp(); } }
		}

		[BsonIgnore]
		string _ns = string.Empty;
		[BsonElement("ns")]
		public virtual string NameSpace
		{
			get { return _ns; }
			set { _ns = value ?? string.Empty; }
		}

		/// <summary>
		/// The change delta, no guaranteed of full object unless it is an insert
		/// </summary>
		[BsonElement("o")]
		public virtual BsonDocument Payload { get; set; }

		/// <summary>
		/// The original object reference.
		/// This does not guarantee full object, infact, it is really only _id
		/// This field is entirely optional and only shows up on update
		/// </summary>
		[BsonElement("o2")]
		public virtual BsonDocument Source { get; set; }

		/// <summary>
		/// Optional field that denotes operation was part of a batch or not
		/// </summary>
		[BsonElement("b")]
		public virtual bool? Batch { get; set; }

		[BsonElement("v"), BsonIgnoreIfDefault]
		public virtual double Version { get; set; }

		[BsonElement("h")]
		public virtual long Hash { get; set; }

		public override string ToString()
		{
			try
			{
				//return this.ToJSON();
				return string.Format(@"{{op:{0},ts:{1},ns:{2},v:{3},h:{4},o:{5}}}",
					Op, TimeStamp.ToDateTime(), NameSpace, Version, Hash, Payload);
			}
			catch (Exception ex)
			{
				_logger.Warn("ToString", ex);
				return string.Format(@"{{op:{0},ts:{1},ns:{2},v:{3},h:{4}}}", 
					Op, TimeStamp, NameSpace, Version, Hash);
			}
		}
	}
}
