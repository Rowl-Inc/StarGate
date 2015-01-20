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
	public class CDataText
	{
		[XmlText]
		public XmlNode[] Text
		{
			get
			{
				var dummy = new XmlDocument();
				return new XmlNode[] { dummy.CreateCDataSection(Value) };
			}
			set
			{
				if (value == null)
					throw new ArgumentNullException();

				var sb = new StringBuilder();
				for (int i = 0; i < value.Length; i++)
					sb.Append(value[i].Value ?? string.Empty);

				Value = sb.ToString();
			}
		}

		[XmlIgnore]
		public string Value { get; set; }
	}
}
