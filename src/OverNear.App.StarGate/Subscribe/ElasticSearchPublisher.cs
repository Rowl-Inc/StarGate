using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.IO;
using System.Xml.Serialization;

using log4net;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Publishes to a fully qualified ElasticSearch path
	/// </summary>
	[Serializable]
	public class ElasticSearchPublisher : Trigger, IPublisher
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public event Action<IPublisher, IContext> OnSuccess;
		public event Action<IPublisher, IContext, Exception> OnError;

		public ElasticSearchPublisher() 
		{
			EndPoint = "http://localhost:9200/";
		}
		public ElasticSearchPublisher(string endpoint)
		{
			EndPoint = endpoint;
		}

		#region EndPoint & helper

		[XmlIgnore]
		string _endpoint;

		[XmlIgnore]
		readonly object _slock = new object();

		[XmlAttribute]
		public string EndPoint
		{
			get { lock (_slock) return _endpoint; }
			set 
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("EndPoint can not be null or blank");

				lock (_slock)
				{
					_endpoint = value;
				}
			}
		}

		#endregion

		#region RouteField & helper

		[XmlIgnore]
		readonly object _rlock = new object();
		[XmlIgnore]
		bool _hasRouteField;
		/// <summary>
		/// Thread safe n fast way to check if RouteField has value
		/// </summary>
		[XmlIgnore]
		public bool HasRouteField
		{
			get { lock (_rlock) return _hasRouteField; }
		}

		[XmlIgnore]
		string _route;
		/// <summary>
		/// If field is anything but null or blank, routing is implied enabled and will be pushed to cluster on first request.
		/// </summary>
		/// <remarks>This field should align with our schema pattern of _sh field or _id if _sh does not exists!</remarks>
		[XmlAttribute]
		public string RouteField
		{
			get { lock (_rlock) return _route; }
			set 
			{
				lock (_rlock)
				{
					_hasRouteField = !string.IsNullOrWhiteSpace(_route = value);
				}
			}
		}

		#endregion

		#region ParentField & helper

		[XmlIgnore]
		readonly object _plock = new object();
		[XmlIgnore]
		bool _hasParent = false;
		/// <summary>
		/// Boolean if the parent field is present
		/// </summary>
		[XmlIgnore]
		public bool HasParentField
		{
			get { lock(_plock) return _hasParent; }
		}

		[XmlIgnore]
		string _parentf;
		/// <summary>
		/// Mark which field will be used for the parent key
		/// </summary>
		[XmlAttribute]
		public string ParentField
		{
			get { return _parentf; }
			set
			{
				lock (_plock)
				{
					_hasParent = !string.IsNullOrWhiteSpace(_parentf = value);
				}
			}
		}

		[XmlIgnore]
		bool _includesParent = false;
		/// <summary>
		/// If true, will keep the Parent field in the body of the obj, excludes by default...
		/// </summary>
		[XmlAttribute]
		public bool IncludeParentField
		{
			get { lock(_plock) return _includesParent; }
			set { lock (_plock) _includesParent = value; }
		}

		#endregion

		class PublishInput
		{
			public string indexName;
			public string typeName;
			public string id;
			public readonly Dictionary<string, string> urlParams = new Dictionary<string, string>();

			public override string ToString() { return this.ToJson(); }
		}

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context", string.Format("ArgumentNullException for: {0}", this.EndPoint));

			int ix = context.Original.NameSpace.IndexOf('.');
			string indexName = context.Original.NameSpace.Remove(ix);
			string typeName = context.Original.NameSpace.Substring(ix + 1);
			Uri uri = new Uri(EndPoint, UriKind.RelativeOrAbsolute);

			string pathAndQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
			string[] levels = pathAndQuery.Split(new[] { '/', '?', '&' }, StringSplitOptions.RemoveEmptyEntries);
			levels = (from l in levels where !string.IsNullOrEmpty(l) && !l.Contains('=') select l).ToArray();
			if (!levels.IsNullOrEmpty())
				indexName = levels.FirstOrDefault();
			if(levels.Length > 1)
				typeName = levels.Skip(1).FirstOrDefault();

			try
			{
				BsonDocument d;
				if (context.Payload != null && context.Payload.IsBsonDocument && (d = context.Payload.AsBsonDocument).Contains("_id"))
				{
					if (!RawDoc)
						PrepBson(d);

					if(HasParentField) 
					{
						string parent = GetParentFieldValueJson(context);
						if (string.IsNullOrWhiteSpace(parent))
							d[ParentField] = DefaultRootId;
					}

					string id = ExtractMongoKey(d["_id"]);
					if(TieBreakDates)
						_tieBreaker = Crc16.ComputeChecksum(Encoding.Unicode.GetBytes(id));

					var pi = new PublishInput
					{
						id = id,
						indexName = indexName,
						typeName = typeName,
					};
					int pix;
					if ((pix = pathAndQuery.IndexOf("?")) > 0)
					{
						string ps = pathAndQuery.Substring(pix + 1);
						if (!string.IsNullOrWhiteSpace(ps))
						{
							(from s in ps.Split('&')
							 where !string.IsNullOrWhiteSpace(s)
							 let kvix = s.IndexOf('=')
							 where ix > 0
							 let o = new { K = s.Remove(kvix), V = s.Substring(kvix + 1) }
							 group o by o.K into og
							 select og.FirstOrDefault()).ForEach(o => pi.urlParams.Add(o.K, o.V));
						}
					}

					PublishHTTP(pi, context);
				}
				else
				{
					var ex = new NotImplementedException("Payload is null, missing, or unable to extract _id from payload");
					if (OnError != null)
						TryEval(() => OnError(this, context, ex));
					_logger.Warn("Execute: " + context, ex);
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

		static string ExtractMongoKey(BsonValue v)
		{
			string id;
			switch (v.BsonType)
			{
				case BsonType.Binary:
					id = v.AsByteArray.ToMongoBase64();
					break;
				case BsonType.String:
					id = v.AsString;
					break;
				default:
					id = v.ToString();
					break;
			}
			return id;
		}

		string GetParentFieldValueJson(IContext context)
		{
			BsonDocument d;
			if (context.Payload.IsBsonDocument && (d = context.Payload.AsBsonDocument) != null && d.Contains(ParentField))
			{
				BsonValue v = d[ParentField];
				string k = ExtractMongoKey(v);
				return HttpUtility.UrlEncode(k);
			}
			else
				return null;
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

		#region PrepBson(...) & helpers

		string ExtracEsDate(BsonValue v)
		{
			DateTime dt;
			switch(v.BsonType) 
			{
				case BsonType.Double:
					dt = Convert.ToInt64(Math.Floor(v.AsDouble/1000)).FromUnixTime();
					break;
				case BsonType.DateTime:
					dt = v.AsBsonDateTime.ToUniversalTime();
					break;
				case BsonType.Int32:
					dt = (v.AsInt32/1000).FromUnixTime();
					break;
				case BsonType.Int64:
					dt = (v.AsInt64/1000).FromUnixTime();
					break;
				default:
					throw new InvalidOperationException("Unable to ExtracEsDate from value type: " + v.BsonType);
			}

			string ds = dt.ToString(@"yyyy\-MM\-dd\THH:mm:ss");
			if (TieBreakDates)
				ds += '.' + _tieBreaker.ToString("D5");

			return ds;
		}

		void PrepBson(BsonDocument d)
		{
			if (d == null || d.IsBsonNull)
				return;

			foreach (BsonElement el in d.Elements.ToArray())
			{
				PrepBson(d, el);
			}
		}

		void PrepBson(BsonDocument parent, string field, BsonDocument d)
		{
			if (d == null || d.IsBsonNull)
				return;

			foreach (BsonElement el in d.Elements.ToArray())
			{
				switch (el.Name)
				{
					case "$binary":
						if (el.Value.IsString)
						{
							string nv = el.Value.AsString.ElasticSearchSafeBase64();
							parent.Remove(field);
							parent.Add(field, nv);
							return;
						}
						break;
					case "$date":
						if (el.Value.IsDouble)
						{
							string ds = ExtracEsDate(el.Value);
							parent.Remove(field);
							parent.Add(field, ds);
							return;
						}
						break;
					default:
						PrepBson(d);
						break;
				}
			}
		}

		void PrepBson(BsonDocument d, BsonElement el)
		{
			if (el == null || el.Value == null || el.Value.IsBsonNull)
				return;

			switch (el.Value.BsonType)
			{
				case BsonType.Document:
					PrepBson(d, el.Name, el.Value.AsBsonDocument);
					break;
				case BsonType.Binary:
					{
						string nv = el.Value.AsByteArray.ToMongoBase64();
						el.Value = nv;
					}
					break;
				case BsonType.DateTime:
					{
						string ds = ExtracEsDate(el.Value);
						el.Value = ds;
					}
					break;
				case BsonType.Array:
					{
						BsonArray arr = el.Value.AsBsonArray;
						for (int i = 0; i < arr.Count; i++)
						{
							PrepBson(arr, i, arr[i]);
						}
					}
					break;
			}
		}

		void PrepBson(BsonArray arr, int i, BsonValue v)
		{
			if (v == null || v.IsBsonNull)
				return;

			switch (v.BsonType)
			{
				case BsonType.Document:
					PrepBson(arr, i, v.AsBsonDocument);
					break;
				case BsonType.Binary:
					{
						string nv = v.AsByteArray.ToMongoBase64();
						arr[i] = nv;
					}
					break;
				case BsonType.DateTime:
					{
						string ds = ExtracEsDate(v);
						arr[i] = ds;
					}
					break;
				case BsonType.Array:
					{
						BsonArray carr = v.AsBsonArray;
						for (int j = 0; j < carr.Count; j++)
						{
							PrepBson(carr, j, carr[j]);
						}
					}
					break;
			}
		}

		void PrepBson(BsonArray parent, int i, BsonDocument d)
		{
			if (d == null || d.IsBsonNull)
				return;

			foreach (BsonElement el in d.Elements)
			{
				switch (el.Name)
				{
					case "$binary":
						if (el.Value.IsString)
						{
							string nv = el.Value.AsString.ElasticSearchSafeBase64();
							parent[i] = nv;
							return;
						}
						break;
					case "$date":
						if (el.Value.IsDouble)
						{
							string ds = ExtracEsDate(el.Value);
							parent[i] = ds;
							return;
						}
						break;
					default:
						PrepBson(d);
						break;
				}
			}
		}

		#endregion

		#region PublishHTTP & routing

		/// <summary>
		/// crc16 of _id
		/// </summary>
		ushort _tieBreaker = 0;

		/// <summary>
		/// When true, will apply _id crc16 to dt milliseconds
		/// </summary>
		[XmlAttribute]
		public bool TieBreakDates { get; set; }

		string GetBaseUri(PublishInput pi)
		{
			string baseUri = EndPoint;
			var uri = new Uri(EndPoint, UriKind.RelativeOrAbsolute);
			if (uri.IsAbsoluteUri)
			{
				int ix = baseUri.IndexOf(uri.PathAndQuery);
				if (ix > 0)
					baseUri = baseUri.Remove(ix);

				baseUri += '/' + pi.indexName + '/' + pi.typeName + '/';
			}
			else
			{
				int ix = baseUri.IndexOf("?");
				if (ix > 0)
					baseUri = baseUri.Remove(ix);
			}
			if (baseUri.Last() != '/')
				baseUri += '/';

			return baseUri;
		}

		string GetRouteFieldValueJson(IContext context)
		{
			BsonDocument d;
			if (context.Payload.IsBsonDocument && (d = context.Payload.AsBsonDocument) != null && d.Contains(RouteField))
			{
				BsonValue v = d[RouteField];
				return HttpUtility.UrlEncode(v.ToJson());
			}
			else
				return null;
		}

		/// <summary>
		/// When true, no prep work on bson will be done
		/// </summary>
		[XmlAttribute]
		public bool RawDoc { get; set; }

		static string GetVerb(OpLogType op)
		{
			switch (op)
			{
				case OpLogType.Insert:
					return "POST";
				case OpLogType.Update:
					return "PUT";
				case OpLogType.Delete:
					return "DELETE";
				default:
					return "GET";
			}
		}

		[XmlIgnore]
		string _defaultRoot = "root";
		/// <summary>
		/// For itemw without roots (recursive situation?) use this method to allow for one
		/// </summary>
		[XmlAttribute]
		public string DefaultRootId
		{
			get { return _defaultRoot; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentOutOfRangeException("DefaultRootId can not be null or blank");

				_defaultRoot = value;
			}
		}

		void PublishHTTP(PublishInput pi, IContext context)
		{
			try
			{
				var baseUri = new StringBuilder(GetBaseUri(pi));
				baseUri.Append(pi.id);

				if (HasRouteField && !pi.urlParams.ContainsKey("routing"))
					pi.urlParams.Add("routing", GetRouteFieldValueJson(context));

				if (HasParentField)
				{
					if (!pi.urlParams.ContainsKey("parent"))
						pi.urlParams.Add("parent", GetParentFieldValueJson(context));
					if (!this.IncludeParentField && context.Payload.IsBsonDocument)
						context.Payload.AsBsonDocument.Remove(this.ParentField);
				}

				if (pi.urlParams.Count > 0)
				{
					baseUri.Append('?');
					pi.urlParams.ForEach(p => baseUri.AppendFormat("{0}={1}&", 
						HttpUtility.UrlEncode(p.Key), 
						HttpUtility.UrlEncode(p.Value)));
					baseUri.Length--;
				}

				string verb = GetVerb(context.Original.Operation);
				if (context.Payload.IsBsonDocument)
				{
					BsonDocument payload = context.Payload.AsBsonDocument;
					if (payload.Contains("op"))
					{
						string op = payload["op"].AsString; //extract actual command if possible
						OpLogType ot = OpLogLine.ParseType(op);
						verb = GetVerb(ot);

						payload.Remove("op"); //no need for this any more
					}
					context.Payload = payload;
				}

				Uri u = context.TaskChain.GetAbsUrl(baseUri.ToString());
				var rb = new RequestBuilder(verb, u, true) { VerboseLog = context.VerboseLog };
				HttpWebRequest request = rb.Send(context);
				if (request == null)
					return;

				using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
				{
					if ((int)response.StatusCode < 300) //ok
					{
						string msg = string.Format("{0} -> {1} {2} ({3})",
							response.ResponseUri,
							response.StatusCode, response.StatusDescription,
							response.ContentType);

						if (context.VerboseLog.HasFlag(VerboseLogLevel.Response))
							_logger.Debug(msg);

						if (OnSuccess != null)
							TryEval(() => OnSuccess(this, context));
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
					TryEval(() => LogError(this, context, response));
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Execute: " + context, ex);
				if (OnError != null)
					TryEval(() => OnError(this, context, ex));

				throw;
			}
		}

		[Serializable]
		class EsResponse
		{
			public bool ok = false;
			public bool found = false;
			public string _index = null;
			public string _type = null;
			public string _id = null;
			public int _version = 0;
		}

		bool IsNotAnError(string json)
		{
			var b = false;
			if (string.IsNullOrWhiteSpace(json))
				return b;

			try
			{
				EsResponse r = json.FromJSON<EsResponse>();
				b = r != null && r.ok && !r.found;
			}
			catch { }
			return b;
		}

		void LogError(IPublisher publisher, IContext context, HttpWebResponse response)
		{
			var msg = new StringBuilder();
			string json = null;
			if (response != null)
			{
				msg.AppendFormat("{0} -> {1} {2} ({3})\r\n",
					response.ResponseUri,
					response.StatusCode, response.StatusDescription,
					response.ContentType);

				using (Stream s = response.GetResponseStream())
				using (var sr = new StreamReader(s))
				{
					json = sr.ReadToEnd();

					s.Flush();
					if (s.CanSeek)
						s.Position = 0;
				}
				msg.Append(json);
			}
			else
				msg.AppendFormat("A publishing error occurred for {0} with {1}", publisher, context);

			if (IsNotAnError(json))
			{
				if (context.VerboseLog.HasFlag(VerboseLogLevel.Response))
					_logger.Info(msg);
			}
			else if (_logger.IsWarnEnabled)
				_logger.Warn(msg);
		}

		#endregion

		public override string ToString()
		{
			return string.Format("EsPub: {0}", EndPoint ?? "<null>");
		}
		
	}
}
