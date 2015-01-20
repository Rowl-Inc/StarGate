using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Serialization;

using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	[XmlRoot("ReadThreads")]
	[XmlInclude(typeof(ReadThread))]
	[Serializable]
	public class ReadThreadList : AbstractConfigCollection<ReadThread>
	{
		public ReadThreadList() : base() { }
		public ReadThreadList(string serializedXml) : base(serializedXml) { }
		public ReadThreadList(FileInfo fileLocation) : base(fileLocation) { }
	}
}
