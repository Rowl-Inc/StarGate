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
using Jint;
using Jint.Runtime;
using Jint.Native;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Take JSON input and JS eval functions that returns { url:"http://test/", verb:"POST" }
	/// to be used as the dynamic endpoint for REST request of the payload
	/// </summary>
	[Serializable]
	public class DynamicRestPublisher : Trigger, IHttpPublisher
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public event Action<IPublisher, IContext> OnSuccess;
		public event Action<IPublisher, IContext, Exception> OnError;

		public event Action<IPublisher, IContext, HttpWebResponse> OnHttpSuccess;
		public event Action<IPublisher, IContext, HttpWebResponse> OnHttpError;

		[XmlIgnore]
		readonly string _fName; //function name

		public DynamicRestPublisher() //serializer compatibility
		{
			int code = this.GetHashCode();
			_fName = "DynamicRest" + code;

			this.OnHttpError += LogError;
		} 
		public DynamicRestPublisher(string jsFunctionLogic) : this()
		{
			JsFunctionLogic = jsFunctionLogic;
		}

		[XmlIgnore]
		readonly object _jslock = new object();
		[XmlIgnore]
		Engine _jscontext;
		[XmlIgnore]
		string _jsFunc;

		[XmlIgnore]
		string _note = string.Empty;
		[XmlAttribute]
		public string Note
		{
			get { return _note; }
			set { _note = value.TrimToEmpty(); }
		}

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
		/// In JavaScript of this format: function(o) { return { url:"http://test/", verb:"POST" }; }
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

		const string URL = "url";
		const string VERB = "verb";
		const string RESET = "reset";

		[XmlAttribute]
		public virtual bool ResetSingletonOnSuccess { get; set; }

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context", "ArgumentNullException for: " + Note);
			if (_jscontext == null)
				throw new InvalidOperationException("JsFunctionLogic is missing or not set! " + Note);

			string json = context.Payload.ToJson(Constants.Instance.STRICT_JSON);
			string js = _fName + '(' + json + ')';
			JsValue jRes;
			lock (_jslock)
			{
				jRes = _jscontext.Execute(js).GetCompletionValue();
			}
			if (jRes.IsNull())
				_logger.Warn("Execute: TryEval(...) returns null. Ignoring this request. " + Note);

			try
			{
				bool publishOk = false;
				if (jRes.IsArray())
				{
					var jcol = jRes.ToObject() as System.Collections.ICollection;
					foreach (object c in jcol)
					{
						if (c == null)
							continue;

						object o;
						if (c is JsValue)
						{
							JsValue jv = (JsValue)c;
							o = jv.ToObject();
						}
						else
							o = c;

						if (HandleSingleObj(o, json, context))
							publishOk = true;
					}
				}
				else if (jRes.IsObject())
					publishOk = HandleSingleObj(jRes.ToObject(), json, context);
				else
					LogIgnore("None Dictionary Response", json, jRes.ToJsonOrNullStr(), context);

				if (publishOk && ResetSingletonOnSuccess) //atomic reset, try this for now
				{
					if (context.VerboseLog.HasFlag(VerboseLogLevel.Request))
						_logger.InfoFormat("TaskChain.Reset(...) {0}", Note);

					context.TaskChain.Reset();
				}
			}
			catch (Exception ex)
			{
				_logger.Error("js eval return type not supported: " + jRes.GetType().FullName, ex);
				throw;
			}
		}

		bool HandleSingleObj(object jRes, string json, IContext context)
		{
			bool publishOk = false;
			string url, verb;
			BsonDocument bson = BsonDocument.Create(jRes);
			bson = bson.FixDouble().AsBsonDocument;
			if (!bson.Contains(URL) || string.IsNullOrWhiteSpace(url = bson[URL].ToString()))
				LogIgnore("Missing URL in response. " + Note, json, jRes.ToJsonOrNullStr(), context);
			else if (!bson.Contains(VERB) || string.IsNullOrWhiteSpace(verb = bson[VERB].ToString()))
				LogIgnore("Missing verb in response. " + Note, json, jRes.ToJsonOrNullStr(), context);
			else
			{
				Uri u = context.TaskChain != null ? context.TaskChain.GetAbsUrl(url) : new Uri(url);
				Publish(u.ToString(), verb, context);
				publishOk = true;
			}
			return publishOk;
		}

		void LogIgnore(string msg, string jsonInput, string jsonResponse, IContext context)
		{
			if(context.VerboseLog.HasFlag(VerboseLogLevel.Request))
				_logger.WarnFormat("Execute Ignore: {0}({1}) -> {2} >> returns {3}", _fName, jsonInput, msg, jsonResponse);
		}

		[XmlAttribute]
		public bool NoPayloadInUri { get; set; }

		void TryEval(Action tryit)
		{
			try
			{
				tryit();
			}
			catch (Exception ex)
			{
				_logger.Error("TryEval: " + tryit.Method.Name, ex);
			}
		}

		void LogError(IPublisher publisher, IContext context, HttpWebResponse response)
		{
			var msg = new StringBuilder();
			if (response != null)
			{
				msg.AppendFormat("{0} [{1}] -> {2} {3} ({4})\r\n",
					ToString(),
					Note,
					response.StatusCode, response.StatusDescription,
					response.ContentType);

				using (Stream s = response.GetResponseStream())
				using (var sr = new StreamReader(s))
				{
					msg.Append(sr.ReadToEnd());

					s.Flush();
					if (s.CanSeek)
						s.Position = 0;
				}
			}
			else
				msg.AppendFormat("A publishing error occurred for {0} with {1}. {2}", publisher, context, Note);

			if (_logger.IsDebugEnabled)
				_logger.Warn(msg);
			else
				Console.Error.WriteLine(msg);
		}

		void Publish(string url, string verb, IContext context)
		{
			try
			{
				var baseUri = new Uri(url);
				var rb = new RequestBuilder(verb, baseUri, NoPayloadInUri) { VerboseLog = context.VerboseLog };
				HttpWebRequest request = rb.Send(context);
				if (request == null)
					return;

				using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
				{
					if ((int)response.StatusCode < 300) //ok
					{
						string msg = string.Format("{0} -> {1} {2} ({3})",
							ToString(),
							response.StatusCode, response.StatusDescription,
							response.ContentType);
						if (context.VerboseLog.HasFlag(VerboseLogLevel.Request))
							_logger.Debug(msg);

						if (OnSuccess != null)
							TryEval(() => OnSuccess(this, context));
						if (OnHttpSuccess != null)
							TryEval(() => OnHttpSuccess(this, context, response));
					}
					else if (OnError != null) //warning about problems...
					{
						var ex = new WebException(
							response.StatusDescription + ':' + response.StatusCode,
							null,
							WebExceptionStatus.UnknownError, response);

						TryEval(() => OnError(this, context, ex));
					}
				}
			}
			catch (WebException wex)
			{
				if (OnError != null)
					TryEval(() => OnError(this, context, wex));

				using (HttpWebResponse response = wex.Response as HttpWebResponse)
				{
					if (OnHttpError != null)
						TryEval(() => OnHttpError(this, context, response));
					else
						TryEval(() => LogError(this, context, response));
				}
			}
			catch (Exception ex)
			{
				_logger.ErrorFormat("Execute ({0}): {1}", ex, context, Note);
				if (OnError != null)
					TryEval(() => OnError(this, context, ex));
			}
		}

		public override string ToString()
		{
			return _fName;
		}
	}
}
