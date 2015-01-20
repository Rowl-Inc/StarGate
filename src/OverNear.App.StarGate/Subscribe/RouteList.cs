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
	[XmlRoot("Routes")]
	[XmlInclude(typeof(Route))]
	[XmlInclude(typeof(JsInclude))]
	[Serializable]
	public class RouteList : AbstractConfigCollection<Route>
	{
		public RouteList() : base() { }
		public RouteList(string serializedXml) : base(serializedXml) { }
		public RouteList(FileInfo fileLocation) : base(fileLocation) { }
	}
}
