using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using NUnit.Framework;
using MongoDB.Bson;
using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;

namespace OverNear.App.StarGate.Tests
{
	/// <summary>
	/// This code is obsolete by CallOnce decorator
	/// </summary>
	[TestFixture, Category("Default")]
	public class SubscriberRoutingTests
	{
		static readonly IMongoRepo DUMMY_REPO;

		static SubscriberRoutingTests()
		{
			DUMMY_REPO = new MongoRepo("Stream");
		}

		[Test]
		public void RouteByNameSpace_Test()
		{
			var sw = new StopWatchTimer("RouteByNameSpace_Test");
			var trigger = new DummyPublisher(); //setup dummy trigger & publish/context results

			const string IS_EVEN_RE = @"[02468]$";
			var route = new RouteByNameSpace(IS_EVEN_RE, OpLogType.Insert, trigger); //only even numbers
			Assert.IsNotNull(route.NameSpace);
			StringAssert.Contains(IS_EVEN_RE, route.NameSpace.ToString());
			Assert.AreSame(trigger, route.Trigger);
			Console.WriteLine(route);

			var chain = new RoutingChain(new[] { route });

			int lastPublishCount = 0; //begin test
			for (int i = 0; i < 20; i++)
			{
				var line = new OpLogLine { NameSpace = "NS.Num" + i, Operation = OpLogType.Insert }; //only name space matters for this test...
				var context = new OpLogContext(line, DUMMY_REPO, DUMMY_REPO.Database.Name, chain);
				Assert.AreSame(line, context.Original);
				TaskProcessState state = route.Evaluate(context);
				if (i % 2 == 0)
				{
					Assert.AreEqual(TaskProcessState.Return, state);
					Assert.AreSame(context, trigger.LastContext);
					Assert.AreEqual(lastPublishCount + 1, trigger.PublishCount);
				}
				else
				{
					Assert.AreEqual(TaskProcessState.Continue, state);
					Assert.AreNotSame(context, trigger.LastContext);
					Assert.AreEqual(lastPublishCount, trigger.PublishCount);
				}
				lastPublishCount = trigger.PublishCount;
			}
			sw.Log(Console.WriteLine);
		}

		class TestData
		{
			public Route Route { get; set; }
			public OpLogContext Input { get; set; }
		}

		ICollection<TestData> GenerateRoutingTests(int count, out IEnumerable<Route> uniqueRoutes)
		{
			var rmap = new Dictionary<string, Route>();
			var tests = new List<TestData>();
			for (int i = 0; i < count; i++)
			{
				const string NS = "Ns_Num";

				string k = '^' + NS + (i % 10);
				Route r;
				if (rmap.ContainsKey(k))
					r = rmap[k];
				else
				{
					var p = new DummyPublisher();
					if (i % 2 == 0)
						r = new RouteByNameSpace(k, OpLogType.Update, p);
					else
					{
						string js = string.Format(@"function(o) 
						{{ 
							var re = /{0}/i;
							return o.op=='u' && re.test(o.ns);
						}};", k);
						var t = new RouteByJsPredicate(js, p);
						r = new RouteByNameSpace('^' + NS, OpLogType.Any, t);
					}
					rmap.Add(k, r);
				}
				var o = new OpLogLine { NameSpace = NS + i, Operation = OpLogType.Update };
				tests.Add(new TestData
				{
					Input = new OpLogContext(o, DUMMY_REPO, null),
					Route = r,
				});
			}
			uniqueRoutes = rmap.Values;
			return tests;
		}

		[Test]
		public void Routing_Test()
		{
			var sw = new StopWatchTimer("Routing_Test");
			IEnumerable<Route> uniqueRoutes;
			ICollection<TestData> tests = GenerateRoutingTests(10, out uniqueRoutes); //setup test data
			var chain = new RoutingChain(uniqueRoutes); //build the logic chain

			int i = 0;
			foreach (TestData d in tests) //do the test here
			{
				Trigger rt = d.Route.Trigger;
				if (rt is RouteByJsPredicate)
				{
					RouteByJsPredicate pd = rt as RouteByJsPredicate;
					Assert.IsNotNull(pd);
					Assert.IsNotNull(pd.Trigger);
					rt = pd.Trigger;
				}
				Assert.IsTrue(rt is DummyPublisher);
				DummyPublisher t = rt as DummyPublisher;
				Assert.IsNotNull(t, d.ToString());
				int ogCount = t.PublishCount;
				IContext ogContext = t.LastContext;

				chain.Evaluate(d.Input); //feed in input
				Assert.AreEqual(ogCount + 1, t.PublishCount, "i=={0}", i); //test for expected outputs
				if (ogContext != null)
					Assert.AreNotSame(ogContext, t.LastContext);

				Assert.AreSame(d.Input, t.LastContext);
				i++;
			}
			sw.Log(Console.WriteLine);
		}

		[Test]
		public void PassThrough_Routing_Test()
		{
			var sw = new StopWatchTimer("PassThrough_Routing_Test");
			var routes = new List<Route>();
			var publishers = new List<DummyPublisher>();
			for (int i = 0; i < 5; i++)
			{
				var p = new DummyPublisher();
				publishers.Add(p);

				if (i % 2 == 0)
				{
					var pd = new RouteByJsPredicate(@"function(o) { return true; }", p);
					routes.Add(new RouteByNameSpace("^[a-z]+", OpLogType.Any, pd) { Continue = true });
				}
				else
					routes.Add(new RouteByNameSpace("^[a-z]+", OpLogType.Insert, p) { Continue = true });
			}

			var chain = new RoutingChain(routes); //build the logic chain
			routes.ForEach(r => CheckTriggerExecCount(r.Trigger, 0, null)); //no trigger has been fired

			var ogline = new OpLogLine { NameSpace = "abc123", Operation = OpLogType.Insert, TimeStamp = new BsonTimestamp(1) };
			var cx = new OpLogContext(ogline, DUMMY_REPO, null);
			for (int i = 0; i < 10; i++)
			{
				chain.Evaluate(cx); //eval only once...
				routes.ForEach(r => CheckTriggerExecCount(r.Trigger, i + 1, cx)); //all triggers are fired once w/ matching context

				DateTime lastPublished = publishers.First().LastPublished; //execution order test
				for (int j = 1; j < publishers.Count; j++)
				{
					DateTime pub = publishers[j].LastPublished;
					Assert.Less(lastPublished, pub);
					lastPublished = pub;
				}
			}
			sw.Log(Console.WriteLine);
		}

		void CheckTriggerExecCount(Trigger t, int expected, IContext lastContext)
		{
			Assert.IsNotNull(t);
			if (t is RouteByJsPredicate)
			{
				RouteByJsPredicate pd = t as RouteByJsPredicate;
				Assert.IsNotNull(pd);
				Assert.IsNotNull(pd.Trigger);
				t = pd.Trigger;
			}
			Assert.IsTrue(t is DummyPublisher);
			DummyPublisher p = t as DummyPublisher;
			
			Assert.IsNotNull(p);

			Assert.AreEqual(expected, p.PublishCount);
			if (lastContext == null)
				Assert.IsNull(p.LastContext);
			else
				Assert.AreEqual(lastContext.ToJson(), p.LastContext.ToJson());
		}
	}
}
