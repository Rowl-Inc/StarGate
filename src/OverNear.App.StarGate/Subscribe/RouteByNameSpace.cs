using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Matches a specific name space to a publisher...
	/// </summary>
	[Serializable]
	public class RouteByNameSpace : Route
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		static readonly Regex IGNORE_NS = new Regex(Settings.ReadFromAppConfig().IgnoreNameSpace, RegexOptions.Compiled);
		
		public RouteByNameSpace() { } //xml serializer compatibility
		public RouteByNameSpace(string nameSpace, OpLogType opLogType, Trigger successTrigger)
		{
			NameSpace = nameSpace;
			OpLogType = opLogType;
			Trigger = successTrigger;
		}

		[XmlIgnore]
		Regex _match;

		[XmlAttribute]
		public string NameSpace
		{
			get { return _match.ToString(); }
			set
			{
				if (value == null)
					throw new ArgumentNullException();
				if (string.IsNullOrWhiteSpace(value.ToString()))
					throw new ArgumentOutOfRangeException("Match RegEx can not be blank");

				_match = new Regex(value, RegexOptions.Compiled | RegexOptions.IgnoreCase);
			}
		}

		[XmlAttribute]
		public OpLogType OpLogType { get; set; }

		public override void Execute(IContext context) { Evaluate(context); }
		public override TaskProcessState Evaluate(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Trigger == null)
				throw new InvalidOperationException("Trigger is null or not set!");

			if (OpLogType.HasFlag(context.Original.Operation) && _match.IsMatch(context.Original.NameSpace))
			{
				if (IGNORE_NS.IsMatch(context.Original.NameSpace))
				{
					if(context.VerboseLog.HasFlag(VerboseLogLevel.Routing))
						_logger.WarnFormat("Evaluate ignoring {0}:{1} input from {2}", context.Original.Op, context.Original.Operation, context.Original.NameSpace);

					return TaskProcessState.Return;
				}
				else
				{
					IContext cx = Continue ? context.Copy() : context;
					if (Trigger is Route)
						return (Trigger as Route).Evaluate(cx); //defer response to next route trigger
					else
					{
						Trigger.Execute(cx);
						return TaskProcessState.Return;
					}
				}
			}
			else
				return TaskProcessState.Continue;
		}

		public override string ToString()
		{
			return string.Format("RouteByNameSpace: /{0}/i", NameSpace);
		}
	}
}
