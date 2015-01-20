using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Configuration;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	public class ConfigSection : IConfigurationSectionHandler
	{
		public object Create(object parent, object configContext, XmlNode section)
		{
			var settings = new Settings(section);
			return settings;
		}
	}
}
