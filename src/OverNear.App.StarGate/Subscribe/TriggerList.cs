using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	[XmlRoot("Triggers")]
	[XmlInclude(typeof(Trigger))]
	[XmlInclude(typeof(RestPublisher))]
	[XmlInclude(typeof(NullPublisher))]
	[XmlInclude(typeof(DynamicRestPublisher))]
	[XmlInclude(typeof(ElasticSearchPublisher))]
	[XmlInclude(typeof(PublishChain))]
	[Serializable]
	public class TriggerList : AbstractConfigCollection<Trigger>
	{
		public TriggerList() : base() { }
		public TriggerList(string serializedXml) : base(serializedXml) { }
		public TriggerList(FileInfo fileLocation) : base(fileLocation) { }
	}
}
