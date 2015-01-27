using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Threading;
using BitArray = System.Collections.BitArray;

using NUnit.Framework;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;

namespace OverNear.App.StarGate.Tests
{
	[TestFixture, Category("Default")]
    public class DomainObjTests
    {
		static readonly IMongoRepo DUMMY_REPO;

		internal static readonly OpLogType[] TRUE_FLAGS;
		static DomainObjTests()
		{
			DUMMY_REPO = new MongoRepo("UnitTest");

			var flags = new List<OpLogType>();
			foreach (OpLogType t in Enum.GetValues(typeof(OpLogType)))
			{
				var arr = new BitArray(new[] { (int)t });
				int maskCount = 0;
				for (int i = 0; i < arr.Length; i++)
				{
					if (arr[i])
					{
						maskCount++;
						if (maskCount > 1)
							break;
					}
				}
				if (maskCount < 2)
					flags.Add(t);
			}
			TRUE_FLAGS = flags.ToArray();
		}

		[Test]
		public void TimeStamp_To_DateTime()
		{
			var sw = new StopWatchTimer("TimeStamp_To_DateTime");
			DateTime og = DateTime.Now;

			BsonTimestamp ts1 = og.ToTimestamp();
			Assert.IsNotNull(ts1);
			Assert.Greater(ts1.Value, 0);

			BsonTimestamp ts2 = og.ToUniversalTime().ToTimestamp();
			Assert.IsNotNull(ts1);
			Assert.Greater(ts2.Value, 0);

			Assert.AreEqual(ts1.Value, ts2.Value);

			DateTime dt1 = ts1.ToDateTime();
			Assert.Greater(dt1, DateTime.MinValue);
			DateTime dt2 = ts2.ToDateTime();
			Assert.Greater(dt2, DateTime.MinValue);

			Assert.AreEqual(dt1, dt2);
			DateTime utc = og.ToUniversalTime();
			Assert.AreEqual(utc.ToString(), dt1.ToString());
			sw.Log(Console.WriteLine);
		}

		[Test]
		public void OpsType_Serialization()
		{
			var sw = new StopWatchTimer("OpsType_Serialization");
			var o = new OpLogLine();
			var tsMap = new Dictionary<OpLogType, string>();
			foreach (OpLogType t in TRUE_FLAGS)
			{
				o.Operation = t;
				string s = o.Op;
				tsMap.Add(t, s);
				o.Op = s;
				Assert.AreEqual(t, o.Operation);
			}
			tsMap.ForEach(p =>
			{
				o.Operation = p.Key;
				Assert.AreEqual(p.Value, o.Op);
			});
			sw.Log(Console.WriteLine);
		}

		[Test]
		public void OpLogLine_Serialization()
		{
			var sw = new StopWatchTimer("OpLogLine_Serialization");
			var optypes = new List<OpLogType>(TRUE_FLAGS);
			
			for (int i = 0; i < optypes.Count; i++)
			{
				var o = new OpLogLine
				{
					Created = DateTime.UtcNow,
					Hash = this.GetHashCode(),
					Operation = optypes[i % optypes.Count],
					Version = i % 9,
					NameSpace = Guid.NewGuid().ToString(),
					Payload = new BsonDocument { { "g", Guid.NewGuid().ToByteArray() } },
				};

				byte[] bson = o.ToBson();
				CollectionAssert.IsNotEmpty(bson);
				OpLogLine b = BsonSerializer.Deserialize<OpLogLine>(bson);
				o.AreEqual(b);

				string json = o.ToJson();
				Assert.IsFalse(string.IsNullOrWhiteSpace(json));
				OpLogLine j = BsonSerializer.Deserialize<OpLogLine>(json);
				o.AreEqual(j);
			}
			sw.Log(Console.WriteLine);
		}

		[Test]
		public void RouteList_Serialization_ToMemory()
		{
			string xml = null;
			RouteList_Serialization(rl => xml = rl.XmlString, () => new RouteList(xml), "RouteList_Serialization_ToMemory");
		}

		[Test]
		public void RouteList_Serialization_ToFile()
		{
			FileInfo f = new FileInfo("RouteList_Serialization_ToFile.xml");
			RouteList_Serialization(rl => rl.SaveToFile(f), () => new RouteList(f), "RouteList_Serialization_ToFile");
		}

		void RouteList_Serialization(Action<RouteList> save, Func<RouteList> reload, string testName = null)
		{
			if (string.IsNullOrWhiteSpace(testName))
				testName = "RouteList_Serialization";

			var sw = new StopWatchTimer(testName);

			var p = new RestPublisher("PUT", "http://localhost");
			var d = new TransformJsDecorator(@"function(o) { return o; }", p);
			var t = new RouteByJsPredicate(@"function(o) { return true; }", d);
			var r = new RouteByNameSpace("ns.abc", OpLogType.Insert, t);

			//var jp = new DynamicRestPublisher(@"function(o) { return {url:""http://localhost/"",verb:""PUT""}; }");
			var jp = new DynamicRestPublisher(@"function(o) { 
	var r = {url:'http://localhost:8080/', verb:'GET'};
	switch(o.op) {
		case 'i':
			r.verb = 'POST';
			break;
		case 'u':
			r.verb = 'PUT';
			break;
		case 'd':
			r.verb = 'DELETE';
			break;
		default:
			r.verb = 'GET';
			break;
	}
	return r; 
};");
			var j = new RouteByNameSpace("ns.xyz", OpLogType.Update, jp);
			var rl = new RouteList { r, j };

			Assert.AreEqual(2, rl.Count);
			Assert.IsFalse(string.IsNullOrWhiteSpace(rl.XmlString));
			save(rl);

			var rc = new RoutingChain(rl);
			Console.WriteLine(rl.XmlString);

			RouteList rl2 = reload();
			Assert.AreEqual(rl.Count, rl2.Count);

			RouteByNameSpace r2 = rl2.First() as RouteByNameSpace;
			Assert.IsNotNull(r2);
			Assert.AreEqual(r.NameSpace, r2.NameSpace);
			Assert.AreEqual(r.OpLogType, r2.OpLogType);
			Assert.AreEqual(r.Continue, r2.Continue);

			RouteByJsPredicate t2 = r2.Trigger as RouteByJsPredicate;
			Assert.IsNotNull(t2);
			Assert.AreEqual(t.JsFunctionLogic.Replace("\r\n", "\n"), t2.JsFunctionLogic);

			TransformJsDecorator d2 = t2.Trigger as TransformJsDecorator;
			Assert.IsNotNull(d2);
			Assert.AreEqual(d.JsFunctionLogic.Replace("\r\n", "\n"), d2.JsFunctionLogic);

			RestPublisher p2 = d2.Trigger as RestPublisher;
			Assert.IsNotNull(p2);
			Assert.AreEqual(p.Verb, p2.Verb);
			Assert.AreEqual(p.EndPoint, p2.EndPoint);

			RouteByNameSpace j2 = rl2.Last() as RouteByNameSpace;
			Assert.IsNotNull(j2);
			Assert.AreEqual(j.NameSpace, j2.NameSpace);
			Assert.AreEqual(j.OpLogType, j2.OpLogType);
			Assert.AreEqual(j.Continue, j2.Continue);

			DynamicRestPublisher p3 = j2.Trigger as DynamicRestPublisher;
			Assert.IsNotNull(p3);
			Assert.AreEqual(jp.JsFunctionLogic.Replace("\r\n", "\n"), p3.JsFunctionLogic);

			sw.Log(Console.WriteLine);
		}

		//[MTAThread]
		[Test]
		public void Test_Setter()
		{
			HttpEcho.Program echo = null;
			System.Threading.Thread thread = null;
			try
			{
				echo = new HttpEcho.Program();
				thread = new System.Threading.Thread(echo.Run) { Name = "ECHO", IsBackground = true };
				thread.Start();
				//System.Threading.Thread.Sleep(3000);

				var manualRest = new ManualResetEvent(false);
				var ft = new Thread(() =>
				{
					var p = new DynamicRestPublisher();
					p.JsFunctionLogic = @"function(o) { 
	var r = {url:'http://localhost:8080/', verb:'GET'};
	switch(o.op) {
		case 'i':
			r.verb = 'POST';
			break;
		case 'u':
			r.verb = 'PUT';
			break;
		case 'd':
			r.verb = 'DELETE';
			break;
		default:
			r.verb = 'GET';
			break;
	}
	return r; 
};";
					var o = new OpLogLine()
					{
						Created = DateTime.UtcNow,
						Hash = this.GetHashCode(),
						NameSpace = "N.S",
						Operation = OpLogType.Insert,
						TimeStamp = new BsonTimestamp(1),
						Version = 1,
						Payload = new BsonDocument { { "_id", Guid.NewGuid() } }
					};
					var cx = new OpLogContext(o, DUMMY_REPO, null);
					try
					{
						p.Execute(cx);
					}
					finally
					{
						manualRest.Set();
					}
				}) { IsBackground = true, Name = "FT" };
				ft.Start();

				WaitHandle.WaitAny(new[] { manualRest });
			}
			finally
			{
				if (echo != null)
					echo.Dispose();
				if (thread != null)
					thread.Join();
			}
		}

		[Test]
		public void Settings_Serialization_Manual()
		{
			var sw = new StopWatchTimer("Settings_Serialization_Manual");

			var s = new Settings();
			s.ReplicaName = Guid.NewGuid().ToString();
			//s.ReadStateRepo = new OverNear.App.StarGate.Repo.ReadStateElasticRepo { };

			//s.ReadStatePath = "mongodb://localhost/whatever/" + Guid.NewGuid() + ".config";
			s.IgnoreNameSpace = "ns." + Guid.NewGuid();

			s.CursorRestartSleepMs = DateTime.UtcNow.Second * 1000;
			s.NoMasterSleepMs = DateTime.UtcNow.Second * 1000;
			s.NoDataSleepMs = DateTime.UtcNow.Second * 1000;

			var rt = new Repo.ReadThread { Match = ".*", Path = "mongo://localhost/" };
			s.ReadThreads.Add(rt);
			s.ReadThreads.Add(new Repo.ReadThread { Match = "^a", Path = "mongo://localhost" });

			var rp = new RestPublisher("PUT", "http://localhost/");
			var ev = new RouteByJsPredicate(@"function(o) { return true; }", rp);
			var ns = new RouteByNameSpace("ns.xyz", OpLogType.Insert, ev) { Continue = false };
			s.Routes.Add(ns);
			s.Routes.Add(new RouteByNameSpace("User.User", OpLogType.Insert, rp) { Continue = true });

			var ep = new ElasticSearchPublisher("http://localhost/bucket/name");
			var pc = new PublishChain(new Trigger[] { rp, ep });
			var json = new { tweet = new { properties = new { message = new { type = "string", store = "yes" } } } }.ToJSON();
			var en = new ElasticIndexDecorator(ep.EndPoint, pc);
			var put = new CallOnceDecorator(ep.EndPoint, en) { Content = new CDataText { Value = json } };
			var cr = new RouteByNameSpace("ns.xyz1234", OpLogType.Writes, put);
			s.Routes.Add(cr);

			string xml = s.ToString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(xml));
			Console.WriteLine(xml);

			var d = new XmlDocument();
			d.LoadXml(xml);
			var s2 = new Settings(d);
			Assert.AreEqual(s.ReplicaName, s2.ReplicaName);

			Assert.AreEqual(s.ReadThreads.Count, s2.ReadThreads.Count);
			Repo.ReadThread rt2 = s.ReadThreads.FirstOrDefault();
			Assert.IsNotNull(rt2);
			Assert.AreEqual(rt.Match, rt2.Match);
			Assert.AreEqual(rt.Path, rt2.Path);

			Assert.AreEqual(s.Routes.Count, s2.Routes.Count);
			s2.Routes.ForEach(DecoratorCheck);

			RouteByNameSpace ns2 = s.Routes.FirstOrDefault() as RouteByNameSpace;
			Assert.IsNotNull(ns2);
			Assert.AreEqual(ns.NameSpace, ns2.NameSpace);
			Assert.AreEqual(ns.OpLogType, ns2.OpLogType);
			Assert.AreEqual(ns.Continue, ns2.Continue);

			RouteByJsPredicate ev2 = ns2.Trigger as RouteByJsPredicate;
			Assert.IsNotNull(ev2);
			Assert.AreEqual(ev.JsFunctionLogic, ev2.JsFunctionLogic);

			RestPublisher rp2 = ev.Trigger as RestPublisher;
			Assert.IsNotNull(rp2);
			Assert.AreEqual(rp.Verb, rp2.Verb);
			Assert.AreEqual(rp.EndPoint, rp2.EndPoint);

			string xml2 = s2.ToString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(xml2));
			Assert.AreEqual(xml, xml2);

			sw.Log(Console.WriteLine);
		}

		/// <summary>
		/// Relies on config file data
		/// </summary>
		[Test]
		public void Settings_Serialization_AppConfig()
		{
			var sw = new StopWatchTimer("Settings_Serialization_AppConfig");

			Settings s = Settings.ReadFromAppConfig();
			Assert.IsNotNull(s);
			CollectionAssert.IsNotEmpty(s.ReadThreads);
			s.ReadThreads.ForEach(rt =>
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(rt.Match));
				Assert.IsFalse(string.IsNullOrWhiteSpace(rt.Path));
			});
			CollectionAssert.IsNotEmpty(s.Routes);
			s.Routes.ForEach(DecoratorCheck);

			sw.Log(Console.WriteLine);
		}

		static void TriggerCheck(Trigger t)
		{
			Assert.IsNotNull(t);
			if (t is RestPublisher)
			{
				RestPublisher rp = t as RestPublisher;
				Assert.IsFalse(string.IsNullOrWhiteSpace(rp.Verb));
				Assert.IsFalse(string.IsNullOrWhiteSpace(rp.EndPoint));
			}
			else if (t is DynamicRestPublisher)
			{
				DynamicRestPublisher dp = t as DynamicRestPublisher;
				Assert.IsFalse(string.IsNullOrWhiteSpace(dp.JsFunctionLogic));
			}
			else if (t is ElasticSearchPublisher)
			{
				ElasticSearchPublisher es = t as ElasticSearchPublisher;
				Assert.IsFalse(string.IsNullOrWhiteSpace(es.EndPoint));
			}
			else if (t is PublishChain)
			{
				PublishChain pc = t as PublishChain;
				CollectionAssert.IsNotEmpty(pc.Publishers);
				foreach (Trigger p in pc.Publishers)
				{
					TriggerCheck(p);
				}
			}
			if (t is Decorator)
				DecoratorCheck(t as Decorator);
		}
		static void DecoratorCheck(Decorator d)
		{
			Assert.IsNotNull(d);
			if (d is TransformJsDecorator)
			{
				TransformJsDecorator tr = d as TransformJsDecorator;
				Assert.IsFalse(string.IsNullOrWhiteSpace(tr.JsFunctionLogic));
			}
			else if (d is RouteByJsPredicate)
			{
				RouteByJsPredicate je = d as RouteByJsPredicate;
				Assert.IsFalse(string.IsNullOrWhiteSpace(je.JsFunctionLogic));
			}
			else if (d is RouteByNameSpace)
			{
				RouteByNameSpace ns = d as RouteByNameSpace;
				Assert.IsFalse(string.IsNullOrWhiteSpace(ns.NameSpace));
				Assert.AreNotEqual(OpLogType.Unknown, ns.OpLogType);
			}
			else if (d is CallOnceDecorator)
			{
				CallOnceDecorator pm = d as CallOnceDecorator;
				Assert.IsNotNull(pm);
			}
			TriggerCheck(d.Trigger);
		}

		[Test]
		public void Settings_Serialization_TestConfigFile()
		{
			var sw = new StopWatchTimer("Settings_Serialization_TestConfigFile");

			Settings s = Settings.ReadFromFile(@".\stargate_test.config");
			Assert.IsNotNull(s);
			CollectionAssert.IsNotEmpty(s.ReadThreads);
			s.ReadThreads.ForEach(rt =>
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(rt.Match));
				Assert.IsFalse(string.IsNullOrWhiteSpace(rt.Path));
			});
			CollectionAssert.IsNotEmpty(s.Routes);
			s.Routes.ForEach(DecoratorCheck);

			sw.Log(Console.WriteLine);
		}
    }
}
