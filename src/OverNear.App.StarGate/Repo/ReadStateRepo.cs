using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace OverNear.App.StarGate.Repo
{
	[Serializable]
	public abstract class ReadStateRepo : IReadStateRepo
	{
		[XmlAttribute]
		public virtual string Path { get; set; }

		public abstract ReadState Load(string id);

		public abstract ICollection<ReadState> Load(IEnumerable<string> ids);

		public abstract bool Create(ReadState state);

		public abstract bool UpdateTimeStamp(string id, MongoDB.Bson.BsonTimestamp newTimeStamp, MongoDB.Bson.BsonTimestamp lastTimeStamp = null);

		public abstract void Clear(string id);

		public abstract void ClearAll();
	}
}
