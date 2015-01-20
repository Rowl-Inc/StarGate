using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Configuration;
using log4net;
using System.Xml;
using System.Xml.Serialization;

using MongoDB.Bson;
using Jint;
using Jint.Runtime;
using Jint.Native;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Take the configured js function with json input, expect Boolean return.
	/// When true or 1, the route is consider a match and anything else, no match is considered.
	/// The provided publisher will get executed when a match is positive.
	/// </summary>
	[Serializable]
	public class RouteByJsPredicate : Route
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		[XmlIgnore]
		readonly string _fName; //function name

		public RouteByJsPredicate() //serializer compatibility
		{
			int code = this.GetHashCode();
			_fName = "RouteEval" + code;
		}
		public RouteByJsPredicate(string jsFunctionLogic, Trigger publisher) : this()
		{
			JsFunctionLogic = jsFunctionLogic;
			Trigger = publisher;
		}

		[XmlIgnore]
		readonly object _jslock = new object();
		[XmlIgnore]
		Engine _jscontext;
		[XmlIgnore]
		string _jsFunc;

		[XmlElement]
		public CDataText Logic
		{
			get
			{
				var l = new CDataText { Value = JsFunctionLogic };
				return l;
			}
			set
			{
				if (value == null)
					throw new ArgumentNullException();

				JsFunctionLogic = value.Value;
			}
		}

		/// <summary>
		/// In JavaScript of this format if route is a match: function(o) { return true /* or 1 */; }
		/// </summary>
		[XmlIgnore]
		public string JsFunctionLogic
		{
			get { lock (_jslock) return _jsFunc; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentOutOfRangeException("JsFunctionLogic can not be null or blank");

				lock (_jslock)
				{
					_jsFunc = value;
					_jscontext = new Engine()
						.SetValue("log", new Action<object>(_logger.Debug))
						.Execute(_fName + " = " + _jsFunc);
				}
			}
		}

		public override void Execute(IContext context) { Evaluate(context); }
		public override TaskProcessState Evaluate(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Trigger == null)
				throw new InvalidOperationException("Trigger is null or not set!");
			if (_jscontext == null)
				throw new InvalidOperationException("JsFunctionLogic is missing or not set!");

			string json = context.Payload.ToJson(Constants.Instance.STRICT_JSON);
			string js = _fName + '(' + json + ')';
			JsValue jRes;
			lock (_jslock)
			{
				jRes = _jscontext.Execute(js).GetCompletionValue();
			}
			if (jRes.IsNull())
				throw new ApplicationException("js eval returns a null object");

			try
			{
				switch (jRes.ToObject().ToJSON().ToLower().Trim())
				{
					case "true":
					case "1":
					case "1.0":
						IContext cx = Continue ? context.Copy() : context;
						if (Trigger is Route)
							return (Trigger as Route).Evaluate(cx); //defer response to next route trigger
						else
						{
							Trigger.Execute(cx);
							return TaskProcessState.Return;
						}
					default:
						return TaskProcessState.Continue;
				}
			}
			catch (Exception ex)
			{
				_logger.Error("js eval Trigger throws", ex);
				throw;
			}
		}

		public override string ToString()
		{
			return string.Format("{0}: {1}", this.GetType().Name, JsFunctionLogic);
		}

	}
}
