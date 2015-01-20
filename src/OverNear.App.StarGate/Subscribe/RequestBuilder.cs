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
using System.Runtime.Caching;
using log4net;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	public class RequestBuilder
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		/// <summary>
		/// <see cref="http://msdn.microsoft.com/en-US/library/system.runtime.caching.memorycache(v=vs.100).aspx"/>
		/// <seealso cref="http://msdn.microsoft.com/en-us/library/dd941872(v=vs.110).aspx"/>
		/// </summary>
		static readonly MemoryCache CACHE_REPO;
		static readonly CacheItemPolicy CACHE_POLICY;

		static RequestBuilder()
		{
			CACHE_REPO = new MemoryCache(typeof(RequestBuilder).Assembly.FullName);
			CACHE_POLICY = new CacheItemPolicy
			{
				SlidingExpiration = TimeSpan.FromMinutes(10), //10 min expiration
			};
		}

		public Uri EndPoint { get; private set; }
		public string Verb { get; private set; }
		public bool NoPayloadInUri { get; private set; }

		public VerboseLogLevel VerboseLog { get; set; }

		public RequestBuilder(string verb, Uri endpoint, bool noPayloadInUri = false)
		{
			Verb = verb;
			EndPoint = endpoint;
			NoPayloadInUri = noPayloadInUri;
		}

		public HttpWebRequest SendPayload(BsonValue payload)
		{
			return SendBodyParams(payload);
		}

		public HttpWebRequest Send(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			HttpWebRequest request;
			switch (Verb)
			{
				case "GET":
				case "DELETE":
				case "HEAD":
					request = SendUrlParams(context);
					break;
				case "POST":
				case "PUT":
				default:
					request = SendBodyParams(context);
					break;
			}
			//_logger.DebugFormat("{0} {1}", Verb, EndPoint == null ? "<null>" : EndPoint.PathAndQuery);
			return request;
		}

		void BuildQueryString(StringBuilder sb, BsonValue v, string parent = null)
		{
			if (v.IsBsonDocument)
			{
				BsonDocument d = v.AsBsonDocument;
				foreach (BsonElement el in d)
				{
					string name = (parent ?? string.Empty) + el.Name;
					sb.Append(HttpUtility.UrlEncode(name));
					sb.Append('=');
					BuildQueryString(sb, el.Value, name + '.');
				}
			}
			else
			{
				if (v.IsBsonBinaryData)
					sb.Append(HttpUtility.UrlEncode(v.AsByteArray));
				else
					sb.Append(HttpUtility.UrlEncode(v.ToString()));
				sb.Append('&');
			}
		}

		string BuildCacheKey(Uri url, string jsonPayload = null)
		{
			var sb = new StringBuilder(Verb);
			sb.Append(' ');
			sb.Append(url);
			if (!string.IsNullOrWhiteSpace(jsonPayload))
			{
				sb.Append(' ');
				sb.Append(jsonPayload);
			}
			return sb.ToString().ToSHA1();
		}

		static readonly Regex HAS_QUERY_STR = new Regex(@"[&?]", RegexOptions.Compiled);
		HttpWebRequest SendUrlParams(IContext context)
		{
			var ub = new UriBuilder(EndPoint);
			if (!NoPayloadInUri && context.Payload != null)
			{
				if (context.Payload.IsBsonDocument)
				{
					var sb = new StringBuilder();
					BuildQueryString(sb, context.Payload);
					if (sb.Length > 1)
						sb.Length--; //remove trailing '&'

					if (!string.IsNullOrWhiteSpace(ub.Query) && ub.Query.Last() != '&')
						ub.Query += '&';

					ub.Query += sb; //always do this
				}
				else
				{
					if (HAS_QUERY_STR.IsMatch(ub.Query))
					{
						if (ub.Query.Last() != '&')
							ub.Query += '&';

						ub.Query += "v=";
					}
					ub.Query += context.Payload.ToString();
				}
			}

			string k = BuildCacheKey(ub.Uri);
			CacheItem ci = CACHE_REPO.GetCacheItem(k);
			if(ci == null || ((DateTime)ci.Value).Add(CACHE_POLICY.SlidingExpiration) < DateTime.UtcNow)
			{
				CACHE_REPO.Set(new CacheItem(k, DateTime.UtcNow), CACHE_POLICY);
				if (VerboseLog == VerboseLogLevel.Request)
					_logger.DebugFormat("{0} {1}", Verb, EndPoint == null ? "<null>" : EndPoint.PathAndQuery);

				HttpWebRequest request = WebRequest.Create(ub.Uri) as HttpWebRequest;
				request.Method = Verb;
				request.ContentType = "application/json";
				request.SetBasicAuthHeader();
				return request;
			}
			else
			{
				if (VerboseLog == VerboseLogLevel.Request)
					_logger.WarnFormat("IGNORING: {0} {1}", Verb, EndPoint == null ? "<null>" : EndPoint.PathAndQuery);

				return null;
			}
		}

		HttpWebRequest SendBodyParams(IContext context)
		{
			return SendBodyParams(context.Payload);
		}

		HttpWebRequest SendBodyParams(BsonValue payload)
		{
			if (payload == null)
				throw new InvalidOperationException("payload can not be null when verb is: " + Verb);

			string json = payload.ToJson(Constants.Instance.STRICT_JSON);

			string k = BuildCacheKey(EndPoint, json);
			CacheItem ci = CACHE_REPO.GetCacheItem(k);
			if (ci == null || ci.Value == null || !(ci.Value is DateTime) || ((DateTime)ci.Value).Add(CACHE_POLICY.SlidingExpiration) < DateTime.UtcNow)
			{
				CACHE_REPO.Set(new CacheItem(k, DateTime.UtcNow), CACHE_POLICY);
				if (VerboseLog == VerboseLogLevel.Request)
					_logger.DebugFormat("{0} {1}", Verb, EndPoint == null ? "<null>" : EndPoint.PathAndQuery);

				byte[] buffer = Encoding.UTF8.GetBytes(json);
				HttpWebRequest request = WebRequest.Create(EndPoint) as HttpWebRequest;
				request.Method = Verb;
				request.ContentType = "application/json";
				request.ContentLength = buffer.Length;
				request.SetBasicAuthHeader();

				using (Stream rs = request.GetRequestStream())
				{
					rs.Write(buffer, 0, buffer.Length);
					rs.Flush();
					rs.Close();
				}
				return request;
			}
			else
			{
				if (VerboseLog == VerboseLogLevel.Request)
					_logger.WarnFormat("IGNORING: {0} {1}", Verb, EndPoint == null ? "<null>" : EndPoint.PathAndQuery);

				return null;
			}
		}
	}
}
