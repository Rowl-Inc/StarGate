using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using NUnit.Framework;
using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;

namespace OverNear.App.StarGate.Tests
{
	[TestFixture, Category("Default")]
    [Category("Integration")]
	public class TransformerDecorationUnitTest
	{
		static readonly IMongoRepo DUMMY_REPO;

		static TransformerDecorationUnitTest()
		{
			DUMMY_REPO = new MongoRepo("UnitTest");
		}

		[Test]
		public void InOut_NoTransform()
		{
			var sw = new StopWatchTimer("InOut_NoTransform");

			const string WRAPPER = "Wrapper";
			var pub = new DummyPublisher();
			
			string jsfunc = "function(o) { return { " + WRAPPER + " : o }; };";
			var dec = new TransformJsDecorator(jsfunc, pub);
			{
				var o = new OpLogLine
				{
					Created = DateTime.UtcNow,
					Hash = this.GetHashCode(),
					NameSpace = "NS.CX",
					Operation = OpLogType.NoOp,
					Version = 2.1,
					Payload = new BsonDocument { { "GUID", Guid.NewGuid().ToString() } },
				};
				IContext context = new OpLogContext(o, DUMMY_REPO, null);
				Func<BsonValue, BsonDocument> extractDocument = v =>
				{
					Assert.IsNotNull(v);
					Assert.IsTrue(v.IsBsonDocument);
					BsonDocument d = v.AsBsonDocument;
					Assert.IsNotNull(d);
					return d;
				};

				BsonDocument ogref = extractDocument(context.Payload);
				Assert.IsNotNull(ogref);
				context.Original.AreEqual(BsonSerializer.Deserialize<OpLogLine>(ogref));

				Assert.AreEqual(0, pub.PublishCount);
				Assert.IsNull(pub.LastContext);

				dec.Execute(context);

				Func<BsonDocument, BsonDocument> extractWrapper = d =>
				{
					Assert.IsNotNull(d);
					Assert.IsTrue(d.Contains(WRAPPER));
					Assert.IsTrue(d[WRAPPER].IsBsonDocument);

					BsonDocument child = d[WRAPPER].AsBsonDocument;
					Assert.IsNotNull(child);
					return child;
				};

				BsonDocument extractedPayload = extractWrapper(extractDocument(context.Payload));
				Assert.IsNotNull(extractedPayload);

				string ogjs = context.Original.ToJson(Constants.Instance.STRICT_JSON);
				BsonDocument ogbd = BsonDocument.Parse(ogjs);
				Assert.IsNotNull(ogbd);
				ogbd.AreEqual(extractedPayload);

				Assert.AreEqual(1, pub.PublishCount);
				Assert.IsNotNull(pub.LastContext);
				Assert.AreSame(context, pub.LastContext);
			}

			sw.Log(Console.WriteLine);
		}
	}
}
