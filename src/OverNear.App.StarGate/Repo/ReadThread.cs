using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	[Serializable]
	public class ReadThread
	{
		[XmlAttribute]
		public string Path { get; set; }

		[XmlAttribute]
		public string Match { get; set; }

		[XmlAttribute]
		public bool MasterOnly { get; set; }

		public override string ToString()
		{
			return this.ToJSON();
		}
	}
}
