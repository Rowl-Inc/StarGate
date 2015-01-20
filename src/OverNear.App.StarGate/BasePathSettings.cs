using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	[Serializable]
	public class BasePathSettings
	{
		[XmlAttribute]
		public string Path { get; set; }
	}
}
