using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	[Serializable]
	public class JsInclude
	{
		[XmlAttribute]
		public bool Cache { get; set; }

		[XmlAttribute]
		public string Path { get; set; }
	}
}
