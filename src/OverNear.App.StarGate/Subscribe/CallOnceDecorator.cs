using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using log4net;
using System.Xml;
using System.Xml.Serialization;
using System.Web;
using System.Net;
using System.IO;

using MongoDB.Bson;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	[Serializable]
	public class CallOnceDecorator : Decorator
	{
		protected static ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public CallOnceDecorator() { } //serializer compatibility
		public CallOnceDecorator(string path, Trigger ingest) : this()
		{
			Path = path;
			base.Trigger = ingest;
		}

		[XmlIgnore]
		string _path;
		/// <summary>
		/// Where to send the put map to
		/// </summary>
		[XmlAttribute]
		public virtual string Path
		{
			get { return _path; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Path can not be null or blank!");

				_path = value;
			}
		}

		const string DEFAULT_VERB = "PUT";
		[XmlIgnore]
		string _verb = DEFAULT_VERB;
		/// <summary>
		/// The verb to use for the request, default is PUT
		/// </summary>
		[XmlAttribute]
		public virtual string Verb
		{
			get { return _verb; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					_verb = DEFAULT_VERB; //put by default...
				else
					_verb = value.Trim().ToUpper();
			}
		}

		const string DEFAULT_CONTENT = "application/json";
		[XmlIgnore]
		string _contentType = DEFAULT_CONTENT;
		/// <summary>
		/// Override default content type of application/json
		/// </summary>
		[XmlAttribute]
		public virtual string ContentType
		{
			get { return _contentType; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					_contentType = DEFAULT_CONTENT;
				else
					_contentType = value.Trim();
			}
		}

		/// <summary>
		/// Content instruction for put map
		/// </summary>
		[XmlElement]
		public virtual CDataText Content { get; set; }

		public override void Reset()
		{
			Interlocked.Exchange(ref _runOnce, 0);
			base.Reset();
		}

		/// <summary>
		/// When true, will not execute child triggers after the first pass
		/// </summary>
		[XmlAttribute]
		public virtual bool NoPassThrough { get; set; }

		//instance wise singleton!
		protected int _runOnce = 0;

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Trigger == null)
				throw new InvalidOperationException("Trigger is null or not set!");

			if (Interlocked.CompareExchange(ref _runOnce, 1, 0) == 0) //only execute once on first run!
			{
				try
				{
					if (string.IsNullOrWhiteSpace(Path))
						throw new InvalidOperationException("Path is null or not set!");

					Uri path = context.TaskChain.GetAbsUrl(Path);
					HttpWebRequest request = WebRequest.Create(path.ToString()) as HttpWebRequest;
					request.ContentType = ContentType;
					request.SetBasicAuthHeader();

					bool hasContent = Content != null && !string.IsNullOrWhiteSpace(Content.Value);
					switch (request.Method = Verb)
					{
						case "PUT":
						case "POST":
							if(hasContent)
							{
								byte[] buffer = hasContent ? Encoding.UTF8.GetBytes(Content.Value) : new byte[0];
								request.ContentLength = buffer.Length;
								using (Stream rs = request.GetRequestStream())
								{
									rs.Write(buffer, 0, buffer.Length);
									rs.Flush();
									rs.Close();
								}
							}
							break;
						default:
							//do nothing, allow blank put
							break;
					}
					using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
					{
						string s;
						using (Stream stream = response.GetResponseStream())
						using (var sr = new StreamReader(stream))
						{
							s = sr.ReadToEnd();
							stream.Flush();
						}
						if(context.VerboseLog.HasFlag(VerboseLogLevel.Response))
							_logger.InfoFormat("{0} [{1}] {2}", ToString(), response.StatusCode, s);
					}
					if(NoPassThrough)
						Trigger.Execute(context); //defer to trigger
				}
				catch (WebException wex)
				{
					string s;
					using (Stream stream = wex.Response.GetResponseStream())
					using (var sr = new StreamReader(stream))
					{
						s = sr.ReadToEnd();
						stream.Flush();
					}
					_logger.ErrorFormat("Execute: Singleton PUT {0} [{1}] {2} | {3}", wex, ToString(), wex.Status, wex.Message, s);
				}
				catch (Exception ex)
				{
					_logger.Error("Execute: Singleton PUT " + (this.Path ?? "<null>"), ex);
				}
			}
			if (!NoPassThrough)
				Trigger.Execute(context); //defer to trigger
		}

		public override string ToString()
		{
			return string.Format("{0}: {1}", this.GetType().Name, Path ?? "<null>");
		}
	}
}
