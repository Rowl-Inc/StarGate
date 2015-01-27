using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Web;

using NUnit.Framework;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;
using RestEchoService = OverNear.App.HttpEcho.Program;
using OverNear.App.HttpEcho.Models;

namespace OverNear.App.StarGate.Tests
{
	[TestFixture, Category("Default")]
	public class RestPublishTests : IDisposable
	{
		static readonly IMongoRepo DUMMY_REPO;
		static RestPublishTests()
		{
			DUMMY_REPO = new MongoRepo("UnitTest");
		}

		readonly Random _rand = new Random();
		readonly RestEchoService _echo;
		readonly Thread _httpThread;

		public RestPublishTests()
		{
			_echo = new RestEchoService(8090);
			_httpThread = new Thread(_echo.Run) { Name = "HTTP", IsBackground = true };
		}

		[TestFixtureSetUp]
		public void Setup()
		{
			Assert.IsNotNull(_echo);
			Assert.IsNotNull(_echo.BasePath);
			Assert.IsFalse(string.IsNullOrWhiteSpace(_echo.BasePath.ToString()));

			Assert.IsNotNull(_httpThread);
			_httpThread.Start();
		}

		~RestPublishTests() { Dispose(); }
		int _disposed = 0;
		[TestFixtureTearDown]
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				if (_echo != null)
					_echo.Dispose();
				if (_httpThread != null && _httpThread.IsAlive)
					_httpThread.Join();
			}
		}

		static readonly string[] BODY_VERBS = new[] { "POST", "PUT" };
		static readonly string[] URI_VERBS = new[] { "GET", "DELETE", "HEAD" };
		static readonly string[] SILENT_VERBS = new[] { "HEAD" };

		[Test]
		public void Test1_StaticPublisher()
		{
			foreach (string verb in BODY_VERBS)
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(verb));
				foreach (OpLogType opType in DomainObjTests.TRUE_FLAGS)
				{
					Console.WriteLine("verb: {0} opType: {1}", verb, opType);

					var publisher = new RestPublisher(verb, _echo.BasePath.ToString());
					OpLogLine op = MockOpLogLine(opType);
					RequestTest(publisher, op, verb, s =>
					{
						OpLogLine o = BsonSerializer.Deserialize<OpLogLine>(s);
						Assert.IsNotNull(o);
						Assert.AreNotSame(op, o);
						op.AreEqual(o);
					});
				}
			}
			foreach (string verb in URI_VERBS)
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(verb));
				foreach (OpLogType opType in DomainObjTests.TRUE_FLAGS)
				{
					Console.WriteLine("verb: {0} opType: {1}", verb, opType);

					var publisher = new RestPublisher(verb, _echo.BasePath.ToString());
					OpLogLine op = MockOpLogLine(opType);
					RequestTest(publisher, op, verb);
				}
			}
		}

		string MakeJsFunction(string verb, string basePath)
		{
			Assert.IsNotNull(verb);
			Assert.IsNotNull(basePath);

			verb = HttpUtility.JavaScriptStringEncode(verb);
			basePath = HttpUtility.JavaScriptStringEncode(basePath);

			string jsf = string.Format(@"function(o) {{
	return {{ url:'{0}', verb:'{1}' }}
}}", basePath, verb);
			return jsf;
		}

		[Test]
		public void Test2_DynamicPublisher()
		{
			foreach (string verb in BODY_VERBS)
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(verb));
				foreach (OpLogType opType in DomainObjTests.TRUE_FLAGS)
				{
					Console.WriteLine("verb: {0} opType: {1}", verb, opType);
					string js = MakeJsFunction(verb, _echo.BasePath.ToString());
					Assert.IsFalse(string.IsNullOrWhiteSpace(js));

					var publisher = new DynamicRestPublisher(js) { NoPayloadInUri = verb == "GET" };
					OpLogLine op = MockOpLogLine(opType);
					RequestTest(publisher, op, verb, s =>
					{
						OpLogLine o = BsonSerializer.Deserialize<OpLogLine>(s);
						Assert.IsNotNull(o);
						Assert.AreNotSame(op, o);
						op.AreEqual(o);
					});
				}
			}
			foreach (string verb in URI_VERBS)
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(verb));
				foreach (OpLogType opType in DomainObjTests.TRUE_FLAGS)
				{
					Console.WriteLine("verb: {0} opType: {1}", verb, opType);
					string js = MakeJsFunction(verb, _echo.BasePath.ToString());
					Assert.IsFalse(string.IsNullOrWhiteSpace(js));

					var publisher = new DynamicRestPublisher(js) { NoPayloadInUri = verb == "GET" };
					OpLogLine op = MockOpLogLine(opType);
					RequestTest(publisher, op, verb);
				}
			}
		}

		OpLogLine MockOpLogLine(OpLogType t)
		{
			CollectionAssert.Contains(DomainObjTests.TRUE_FLAGS, t);
			return new OpLogLine
			{
				Created = DateTime.UtcNow,
				Hash = _rand.Next(),
				NameSpace = Guid.NewGuid().ToString(),
				Operation = t,
				Version = (double)_rand.Next(0, 20) / 10,
				TimeStamp = new BsonTimestamp((int)DateTime.UtcNow.ToUnixTime(), _rand.Next()),
				Payload = new BsonDocument
				{
					{ "_id", Guid.NewGuid() },
				},
			};
		}

		void RequestTest<T>(T publisher, OpLogLine op, string verb, Action<string> parsedContent = null)
			where T : Trigger, IHttpPublisher
		{
			Assert.IsNotNull(publisher);
			Assert.IsNotNull(op);
			Assert.IsFalse(string.IsNullOrWhiteSpace(verb));

			if (!SILENT_VERBS.Contains(verb))
			{
				publisher.OnHttpSuccess += (p, cx, r) => PublisherResponse(p, cx, r, s =>
				{
					Assert.IsFalse(string.IsNullOrWhiteSpace(s));
					Assert.AreSame(publisher, p);
					EchoResponse rsp = s.FromJSON<EchoResponse>();
					Assert.IsNotNull(rsp);

					Assert.AreEqual(verb, rsp.Method);
					Assert.IsNotNull(rsp.RequestUri);

					CollectionAssert.IsNotEmpty(rsp.Headers);

					string uri = rsp.RequestUri.ToString();
					string baseUri = _echo.BasePath.ToString();
					StringAssert.StartsWith(baseUri, uri);

					Assert.IsNotNull(rsp.Content);
					if (parsedContent != null)
					{
						Assert.AreEqual(baseUri.Length, uri.Length);
						Assert.IsFalse(string.IsNullOrWhiteSpace(rsp.Content.Text));
						parsedContent(rsp.Content.Text);
					}
					else
					{
						if (verb == "GET" && publisher is DynamicRestPublisher)
						{
							Assert.AreEqual(baseUri.Length, uri.Length);
							Assert.IsTrue(string.IsNullOrWhiteSpace(rsp.RequestUri.Query));
						}
						else
						{
							Assert.Greater(uri.Length, baseUri.Length);
							Assert.IsFalse(string.IsNullOrWhiteSpace(rsp.RequestUri.Query));

							Assert.IsNotNull(op.Payload);
							var queryMap = (from ss in rsp.RequestUri.Query.Split('?', '&')
											where !string.IsNullOrWhiteSpace(ss)
											let ps = ss.Split('=')
											select new
											{
												Key = HttpUtility.UrlDecode(ps.FirstOrDefault()),
												Value = HttpUtility.UrlDecode(ps.Length == 2 ? ps.LastOrDefault() : string.Empty),
											}).ToArray();
							CollectionAssert.IsNotEmpty(queryMap);

							//Assert.AreEqual(op.Payload.ElementCount, queryMap.Length);
							//queryMap.ForEach(kv =>
							//{
							//	Assert.IsTrue(op.Payload.Contains(kv.Key));
							//	Assert.AreEqual(kv.Value, op.Payload[kv.Key].ToString());
							//});
						}
					}
				});
			}
			publisher.OnHttpError += (p, cx, r) => PublisherResponse(p, cx, r, s =>
			{
				Assert.AreSame(publisher, p);
				Assert.Fail(s);
			});
			var dummyContext = new OpLogContext(op, DUMMY_REPO, null);
			publisher.Execute(dummyContext);
		}

		void PublisherResponse<T>(T p, IContext cx, System.Net.HttpWebResponse r, Action<string> str)
			where T : IPublisher
		{
			Assert.IsNotNull(p);
			Assert.IsNotNull(cx);
			Assert.IsNotNull(r);
			Assert.IsNotNull(str);

			using (Stream s = r.GetResponseStream())
			{
				Assert.IsNotNull(s);
				Assert.IsTrue(s.CanRead);
				if (s.CanSeek)
					Assert.Greater(s.Length, 0);

				using (var sr = new StreamReader(s))
				{
					string result = sr.ReadToEnd();
					str(result);
				}
			}
		}
	}
}
