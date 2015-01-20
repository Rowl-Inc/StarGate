using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Threading;
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
	/// Take JSON input and transform it using the configured JS function provided
	/// </summary>
	[Serializable]
	public class TransformJsDecorator : Decorator
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		
		[XmlIgnore]
		readonly string _fName; //function name

		public TransformJsDecorator() //for serializer compatibility
		{
			int code = this.GetHashCode();
			_fName = "Transformer" + code;
		} 
		public TransformJsDecorator(string jsFunctionLogic, Trigger ingest) : this()
		{
			JsFunctionLogic = jsFunctionLogic;
			Trigger = ingest;
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
		/// In JavaScript of this format: function(o) { return o; }
		/// </summary>
		[XmlIgnore]
		public string JsFunctionLogic
		{
			get { lock(_jslock) return _jsFunc; }
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

		[XmlAttribute]
		public bool IgnoreNullEval { get; set; }

		public override void Execute(IContext context) //don't catch/re-throw here, do it at the upper layer
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
			{
				if (IgnoreNullEval)
					return;
				else
					throw new ApplicationException("js eval returns a null object");
			}
			try
			{
				BsonValue v;
				if (jRes.IsObject())
					v = BsonDocument.Create(jRes.ToObject());
				else
					v = BsonValue.Create(jRes.ToObject());

				context.Payload = v.FixDouble();
				Trigger.Execute(context); //do this at the very end...
			}
			catch (Exception ex)
			{
				_logger.Error("js eval return type not supported: " + jRes.GetType().FullName, ex);
				throw;
			}
		}

		public override string ToString()
		{
			return string.Format("{0}: {1}", this.GetType().Name, JsFunctionLogic);
		}
	}
}
