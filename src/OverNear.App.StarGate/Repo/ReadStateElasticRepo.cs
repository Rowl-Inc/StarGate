using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	[Serializable]
	public class ReadStateElasticRepo : ReadStateRepo
	{
		protected static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		#region execute helpers

		class RequestInput
		{
			public RequestInput(string path)
			{
				Path = path;
			}

			string _path;
			public string Path
			{
				get { return _path; }
				set
				{
					if (string.IsNullOrWhiteSpace(value))
						throw new ArgumentException("Path can not be null or blank!");

					_path = value;
				}
			}

			string _verb = "GET";
			public string Verb
			{
				get { return _verb; }
				set
				{
					if (string.IsNullOrWhiteSpace(value))
						throw new ArgumentException("Verb can not be null or blank!");

					_verb = value.Trim().ToUpper();
				}
			}

			public string Data { get; set; }

			string _type = "application/json";
			public string ContentType
			{
				get { return _type; }
				set
				{
					if (string.IsNullOrWhiteSpace(value))
						throw new ArgumentException("ContentType can not be null or empty!");

					_type = value;
				}
			}
		}

		static int _initState = 0;
		void RepoInit()
		{
			if (Interlocked.CompareExchange(ref _initState, 1, 0) == 0)
			{
				try //shard configuration
				{
					var settings = new RequestInput(BasePath + '/' + Index)
					{
						Verb = "PUT",
						Data = new EsSettings
						{
							index = new Dictionary<string, object>
							{
								{ "number_of_shards", 1 },
							}
						}.ToJSON(),
					};
					HttpWebRequest srq = Request(settings);
					Execute(srq);
				}
				catch (Exception ex)
				{
					_logger.Warn("RepoInit: ShardConfig", ex);
				}
			}
		}

		HttpWebRequest Request(RequestInput ri)
		{
			if (ri == null)
				throw new ArgumentNullException("ri");

			RepoInit();

			HttpWebRequest request = WebRequest.Create(ri.Path) as HttpWebRequest;
			request.Method = ri.Verb;
			request.SetBasicAuthHeader();
			if (!string.IsNullOrWhiteSpace(ri.Data))
			{
				request.ContentType = ri.ContentType;

				byte[] buffer = Encoding.UTF8.GetBytes(ri.Data);
				request.ContentLength = buffer.Length;
				using (Stream rs = request.GetRequestStream())
				{
					rs.Write(buffer, 0, buffer.Length);
					rs.Flush();
					rs.Close();
				}
			}
			return request;
		}

		static void Execute(HttpWebRequest request, bool rethrow = false)
		{
			if (request == null)
				throw new InvalidOperationException("request == null");

			try
			{
				using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
				{
				}
			}
			catch (WebException wex)
			{
				string s;
				using (Stream stm = wex.Response.GetResponseStream())
				using (var sr = new StreamReader(stm))
				{
					s = sr.ReadToEnd();
					stm.Flush();
				}
				//EsError e = s.FromJSON<EsError>();
				if (rethrow)
				{
					if (!string.IsNullOrWhiteSpace(s))
						_logger.ErrorFormat("Execute {0} [{1}] {2} | {3}", 
							request.RequestUri.ToString(), wex.Status, wex.Message, s);
					else
						_logger.ErrorFormat("Execute {0}", wex, request.RequestUri.ToString());

					throw;
				}
				else
				{
					if (!string.IsNullOrWhiteSpace(s))
						_logger.WarnFormat("Execute {0} [{1}] {2} | {3}", request.RequestUri.ToString(), wex.Status, wex.Message, s);
					else
						_logger.WarnFormat("Execute {0}", wex, request.RequestUri.ToString());
				}
			}
			catch (Exception ex)
			{
				if (rethrow)
				{
					_logger.ErrorFormat("Execute", ex);
					throw;
				}
				else
					_logger.WarnFormat("Execute", ex);
			}
		}
		static T Execute<T>(HttpWebRequest request, bool rethrow = false)
		{
			if (request == null)
				throw new InvalidOperationException("request == null");

			T res = default(T);
			try
			{
				using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
				{
					string s;
					using (Stream stm = response.GetResponseStream())
					using (var sr = new StreamReader(stm))
					{
						s = sr.ReadToEnd();
						stm.Flush();
					}
					if (!string.IsNullOrWhiteSpace(s))
						res = s.FromJSON<T>();
				}
			}
			catch (WebException wex)
			{
				string s;
				if (wex.Response == null)
					s = wex.Message;
				else
				{
					using (Stream stm = wex.Response.GetResponseStream())
					using (var sr = new StreamReader(stm))
					{
						s = sr.ReadToEnd();
						stm.Flush();
					}
				}
				//EsError e = s.FromJSON<EsError>();
				if (rethrow)
				{
					if (!string.IsNullOrWhiteSpace(s))
						_logger.ErrorFormat("Execute<{0}> {1} {2} [{3}] {4} | {5}", 
							typeof(T).Name, request.Method, request.RequestUri.ToString(), wex.Status, wex.Message, s);
					else
						_logger.ErrorFormat("Execute<{0}> {1} {2}", wex, typeof(T).Name, request.Method, request.RequestUri.ToString());

					throw;
				}
				else
				{
					if (!string.IsNullOrWhiteSpace(s))
						_logger.WarnFormat("Execute<{0}> {1} {2} [{3}] {4} | {5}", 
							typeof(T).Name, request.Method, request.RequestUri.ToString(), wex.Status, wex.Message, s);
					else
						_logger.WarnFormat("Execute<{0}> {1} {2}", wex, typeof(T).Name, request.Method, request.RequestUri.ToString());
				}
			}
			catch (Exception ex)
			{
				if (rethrow)
				{
					_logger.ErrorFormat("Execute<{0}> {1} {2}", ex, typeof(T).Name, request.Method, request.RequestUri.ToString());
					throw;
				}
				else
					_logger.WarnFormat("Execute<{0}> {1} {2}", ex, typeof(T).Name, request.Method, request.RequestUri.ToString());
			}
			return res;
		}

		#endregion

		[XmlIgnore]
		readonly object _plock = new object();

		[XmlIgnore]
		string _path = "http://tweb_01.or.overnear.com:9200/stargate/" + typeof(ReadState).Name;
		/// <summary>
		/// Base path for ES includes Index and Type
		/// </summary>
		[XmlAttribute]
		public override string Path
		{
			get { lock(_plock) return _path; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Path can not be null or blank");

				if (!value.StartsWith("http"))
					throw new InvalidOperationException("Path does not start with http");

				string p;
				if (value.Last() == '/')
					p = value.Remove(value.Length - 1);
				else
					p = value;

				var url = new UriBuilder(p);
				string[] levels = (from l in url.Path.Split('/', '?', '&')
								   where !string.IsNullOrWhiteSpace(l)
								   select l).Take(2).ToArray();
				if (levels.Length != 2)
					throw new ArgumentException("Index & Type are required in Path!");

				Uri u = url.Uri;
				BasePath = u.ToString().Replace(u.PathAndQuery, string.Empty);

				lock (_plock)
				{
					Index = levels.FirstOrDefault();
					Type = levels.LastOrDefault();

					url.Path = '/' + Index + '/' + Type;
					url.Query = string.Empty;
					_path = url.Uri.ToString();
				}
			}
		}

		[XmlIgnore]
		string _basePath;
		[XmlIgnore]
		protected string BasePath
		{
			get { return _basePath; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("BasePath can not be null or blank");

				lock (_plock)
					_basePath = value.Trim();
			}
		}

		[XmlIgnore]
		string _index;
		/// <summary>
		/// Should be auto extracted by Path set
		/// </summary>
		[XmlIgnore]
		protected string Index
		{
			get { lock(_plock) return _index; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Index can not be null or blank");

				lock(_plock)
					_index = value.Trim().ToLower();
			}
		}

		[XmlIgnore]
		string _type;
		/// <summary>
		/// Should be auto extracted by Path set
		/// </summary>
		[XmlIgnore]
		protected string Type
		{
			get { lock(_plock) return _type; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Type can not be null or blank");

				lock(_plock)
					_type = value.Trim();
			}
		}

		[Serializable]
		internal class EsCore
		{
			[JsonProperty("_index")]
			public string Index { get; set; }

			[JsonProperty("_type")]
			public string Type { get; set; }

			[JsonProperty("_id")]
			public string Id { get; set; }

			[JsonProperty("_version")]
			public long Version { get; set; }
		}

		[Serializable]
		internal class EsResult : EsCore
		{
			[JsonProperty("exists")]
			public bool Exists { get; set; }

			[JsonProperty("_source")]
			internal RS Source { get; set; }

			[JsonProperty("ok")]
			public bool OK { get; set; }

			[JsonProperty("found")]
			public bool Found { get; set; }
		}

		//last version fetched from the server
		//[XmlIgnore]
		//long _version = 0;

		//[XmlIgnore]
		static readonly ConcurrentDictionary<string, long> _versions = new ConcurrentDictionary<string, long>();

		/// <summary>
		/// Fetch ReadState by id
		/// </summary>
		/// <param name="id">none blank none null id</param>
		/// <returns>nullable readstate</returns>
		public override ReadState Load(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");

			id = id.ToLower().Trim();
			var f = new RequestInput(this.Path + '/' + id) { Verb = "GET", };
			HttpWebRequest rq = Request(f);
			EsResult rs = Execute<EsResult>(rq);
			if (rs != null && rs.Source != null)
			{
				if (rs.Exists)
					_versions.AddOrUpdate(id, rs.Version, (k, v) => v < rs.Version ? rs.Version : v);
					//Interlocked.Exchange(ref _version, rs.Version);

				return new ReadState(rs.Source);
			}
			else
				return null;
		}

		/// <summary>
		/// This just do individual requests instead of read batch
		/// </summary>
		/// <param name="ids">bunch of none blank none null ids</param>
		/// <returns>bunch of ReadState</returns>
		public override ICollection<ReadState> Load(IEnumerable<string> ids)
		{
			if (ids == null)
				throw new ArgumentNullException("ids");

			return (from id in ids
					where !string.IsNullOrWhiteSpace(id)
					let r = Load(id)
					where r != null
					select r).ToArray();
		}

		[Serializable]
		internal class EsSettings
		{
			public Dictionary<string, object> index;
			public Dictionary<string, object> analysis;
		}

		/// <summary>
		/// Try Create a new ReadState, will return false if it is not successful
		/// </summary>
		/// <param name="state">ReadState that we are trying to remove</param>
		/// <returns></returns>
		public override bool Create(ReadState state)
		{
			if (state == null)
				throw new ArgumentNullException("state");

			var f = new RequestInput(this.Path + '/' + state.Id)
			{
				Verb = "POST",
				Data = state.ToRs().ToJSON(),
			};
			HttpWebRequest rq = Request(f);
			EsResult rs = Execute<EsResult>(rq);

			bool ok = rs != null && rs.OK;
			if (ok && rs.Version > 0)
				_versions.AddOrUpdate(state.Id, rs.Version, (k, v) => v < rs.Version ? rs.Version : v);

			return ok;
		}

		/// <summary>
		/// Only update timestamp if item exists
		/// </summary>
		/// <returns></returns>
		public override bool UpdateTimeStamp(string id, BsonTimestamp newTimeStamp, BsonTimestamp lastTimeStamp = null)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");
			if (newTimeStamp == null)
				throw new ArgumentNullException("newTimeStamp");
			if (lastTimeStamp != null && lastTimeStamp > newTimeStamp)
				throw new ArgumentOutOfRangeException("lastTimeStamp can not be larger than newTimeStamp");

			//id = id.ToLower().Trim();
			//long rqv = Interlocked.Read(ref _version);
			var ts = new ReadState { Id = id, TimeStamp = newTimeStamp, };

			string path = this.Path + '/' + id;
			long rqv = 0;
			if (_versions.TryGetValue(ts.Id, out rqv) && rqv > 0)
				path += "?version=" + rqv;

			var f = new RequestInput(path)
			{
				Verb = "PUT",
				Data = ts.ToRs().ToJSON(),
			};
			HttpWebRequest rq = Request(f);

			EsResult rs = Execute<EsResult>(rq, true);
			bool ok = rs != null && rs.OK && rs.Version == rqv + 1;
			if (rs != null && rs.Version > 0)
				_versions.AddOrUpdate(ts.Id, rs.Version, (k, v) => v < rs.Version ? rs.Version : v);

			return ok;
		}

		/// <summary>
		/// Delete a specific key
		/// </summary>
		/// <param name="id">key to be removed</param>
		public override void Clear(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");

			id = id.ToLower().Trim();
			var f = new RequestInput(this.Path + '/' + id) { Verb = "DELETE", };
			HttpWebRequest rq = Request(f);
			try
			{
				Execute(rq);
			}
			finally
			{
				long v;
				_versions.TryRemove(id, out v);
			}
		}

		/// <summary>
		/// Nuke the entire repo
		/// </summary>
		public override void ClearAll()
		{
			var f = new RequestInput(this.Path) { Verb = "DELETE", };
			HttpWebRequest rq = Request(f);
			try
			{
				Execute(rq);
			}
			finally
			{
				_versions.Clear();
			}
		}
	}
}
