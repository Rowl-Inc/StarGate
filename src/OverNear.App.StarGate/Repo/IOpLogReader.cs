using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson;
using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Repo
{
	public interface IOpLogReader : IDisposable
	{
		event Action<IOpLogReader, BsonTimestamp> OnFoundNewTimestamp;
		long TotalReads { get; }
		void Read(ref BsonTimestamp bsonTimeStamp, Action<OpLogLine> fetchNext);

		IMongoRepo CurrentRepo { get; }

		VerboseLogLevel VerboseLog { get; }
	}
}
