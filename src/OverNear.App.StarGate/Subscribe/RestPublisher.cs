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
	/// <summary>
	/// Take JSON input and push to a rest endpoint using the configured VERB and Uri path
	/// </summary>
	[Serializable]
	public class RestPublisher : Trigger, IHttpPublisher
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public event Action<IPublisher, IContext> OnSuccess;
		public event Action<IPublisher, IContext, Exception> OnError;

		public event Action<IPublisher, IContext, HttpWebResponse> OnHttpSuccess;
		public event Action<IPublisher, IContext, HttpWebResponse> OnHttpError;

		public RestPublisher() //for serializer compatibility
		{
			this.OnHttpError += LogError;
		} 
		public RestPublisher(string verb, string endpoint) : this()
		{
			Verb = verb;
			EndPoint = endpoint;
		}

		[XmlIgnore]
		string _verb;

		[XmlAttribute]
		public string Verb
		{
			get { return _verb; }
			set
			{
				if(string.IsNullOrWhiteSpace(value))
					throw new ArgumentOutOfRangeException("verb is required");

				_verb = value.Trim().ToUpper();
			}
		}

		[XmlIgnore]
		string _endpoint;
		[XmlAttribute]
		public string EndPoint
		{
			get { return _endpoint; }
			set
			{
				if (value == null || string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("endpoint can not be null or blank");

				_endpoint = value; //parse evaluation
			}
		}

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
				msg.AppendFormat("{0} -> {1} {2} ({3})\r\n",
					ToString(),
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
				msg.AppendFormat("A publishing error occured for {0} with {1}", publisher, context);

			_logger.Warn(msg);
		}

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			try
			{
				Uri baseUri = context.TaskChain.GetAbsUrl(EndPoint);
				var rb = new RequestBuilder(Verb, baseUri) { VerboseLog = context.VerboseLog };
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

						if (context.VerboseLog.HasFlag(VerboseLogLevel.Response))
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
				_logger.Error("Execute: " + context, ex);
				if (OnError != null)
					TryEval(() => OnError(this, context, ex));

				//throw;
			}
		}

		public override string ToString()
		{
			return string.Format("RestPublisher: {0} {1}", Verb, EndPoint);
		}
	}
}
