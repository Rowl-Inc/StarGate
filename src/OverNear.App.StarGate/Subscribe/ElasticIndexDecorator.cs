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
	public class ElasticIndexDecorator : CallOnceDecorator
	{
		public ElasticIndexDecorator() : base() { }
		public ElasticIndexDecorator(string path, Trigger ingest)
			: base(path, ingest)
		{
		}

		[XmlIgnore]
		int _ensureOnce = 0;

		public override void Reset()
		{
			Interlocked.Exchange(ref _ensureOnce, 0);
			base.Reset();
		}

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context", string.Format("ArgumentNullException for: {0}", this.Path));
			if (Trigger == null)
				throw new InvalidOperationException(string.Format("Trigger is null or not set! {0}", this.Path));

			int code = 0;
			bool firstRun = false;
			if (Interlocked.CompareExchange(ref _ensureOnce, 1, 0) == 0)
			{
				try
				{
					Uri path = context.TaskChain.GetAbsUrl(Path);
					HttpWebRequest request = WebRequest.Create(path.ToString()) as HttpWebRequest;
					request.Method = "HEAD";
					request.SetBasicAuthHeader();
					using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
					{
						code = (int)response.StatusCode;
					}
				}
				catch (WebException wex)
				{
					if (wex.Message.Contains("(404)"))
						code = 404;
					else
					{
						code = int.MaxValue;
						string s;
						using (Stream stream = wex.Response.GetResponseStream())
						using (var sr = new StreamReader(stream))
						{
							s = sr.ReadToEnd();
							stream.Flush();
						}
						_logger.ErrorFormat("Execute: EnsureIndex {0} [{1}] {2} | {3}", wex, ToString(), wex.Status, wex.Message, s);
					}
				}
				catch (Exception ex)
				{
					code = int.MaxValue;
					_logger.Error("Execute: EnsureIndex " + this.Path, ex);
				}
				finally
				{
					firstRun = true;
					_logger.InfoFormat("Execute: EnsureIndex [HEAD == {0}] {1}", code, Path);
				}
			}
			else
				code = 201;

			if (firstRun || (!firstRun && !NoPassThrough))
			{
				if (code >= 300 && code < int.MaxValue)
					base.Execute(context);
				else
					Trigger.Execute(context); //skip the ensure!
			}
		}

	}
}
