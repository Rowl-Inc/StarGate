using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using System.Web;
using System.Net;
using System.IO;
using System.Xml.Serialization;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	[Serializable]
	public class NullPublisher : Trigger
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public NullPublisher()
		{

		}

		[XmlIgnore]
		string _note = string.Empty;
		[XmlAttribute]
		public string Note
		{
			get { return _note; }
			set { _note = value.TrimToEmpty(); }
		}

		public override void Execute(IContext context)
		{
			if(context.VerboseLog.HasFlag(VerboseLogLevel.Routing))
				_logger.InfoFormat("Execute Completed. {0}", Note);
		}
	}
}
