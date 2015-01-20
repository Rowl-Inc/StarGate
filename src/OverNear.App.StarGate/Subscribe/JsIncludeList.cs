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
	[XmlRoot("Includes")]
	[XmlInclude(typeof(JsInclude))]
	[Serializable]
	public class JsIncludeList : AbstractConfigCollection<JsInclude>
	{
		public JsIncludeList() : base() { }
		public JsIncludeList(string serializedXml) : base(serializedXml) { }
		public JsIncludeList(FileInfo fileLocation) : base(fileLocation) { }
	}
}
