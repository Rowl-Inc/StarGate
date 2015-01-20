using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;
using MongoDB.Bson;
using OverNear.Infrastructure;
using OverNear.App.StarGate.Subscribe;

namespace OverNear.App.StarGate.Tests
{
	public static class Extensions
	{
		public static void AreEqual(this OpLogLine a, OpLogLine b)
		{
			if (a == null)
				Assert.IsNull(b);
			else
			{
				Assert.IsNotNull(b);
				Assert.AreEqual(a.Created.ToString(), b.Created.ToString());
				Assert.AreEqual(a.Hash, b.Hash);
				Assert.AreEqual(a.NameSpace, b.NameSpace);
				Assert.AreEqual(a.Op, b.Op);
				Assert.AreEqual(a.Operation, b.Operation);
				Assert.AreEqual(a.TimeStamp, b.TimeStamp);
				Assert.AreEqual(a.Version, b.Version);

				if (a.Payload == null)
					Assert.IsNull(b.Payload);
				else
					Assert.AreEqual(a.Payload.ToJson(), b.Payload.ToJson());
			}
		}

		public static void AreEqual(this BsonValue a, BsonValue b)
		{
			if (a == null)
				Assert.IsNull(b);
			else
			{
				Assert.IsNotNull(b);
				if (a.IsBsonNull)
					Assert.IsTrue(b.IsBsonNull);
				else
				{
					if (a.IsBsonDocument && b.IsBsonDocument)
						a.AsBsonDocument.AreEqual(b.AsBsonDocument);
					else if (a.IsBsonTimestamp && b.IsBsonDocument) //special case
						a.AsBsonTimestamp.AreEqual(b.AsBsonDocument);
					else if (a.IsBsonDocument && b.IsBsonTimestamp) //special case, reversed
						b.AsBsonTimestamp.AreEqual(a.AsBsonDocument);
					else //all cases
					{
						if (a.IsDouble || b.IsDouble)
						{
							double ast = double.Parse(a.ToString());
							double bst = double.Parse(b.ToString());
							Assert.AreEqual(ast, bst);
						}
						else if (a == null)
							Assert.IsNull(b);
						else
						{
							Assert.AreEqual(a.GetType().FullName, b.GetType().FullName);
							Assert.AreEqual(a.ToString(), b.ToString());
						}
					}
				}
			}
		}

		public static void AreEqual(this BsonTimestamp a, BsonDocument b)
		{
			Assert.IsTrue(b.Contains("$timestamp"));
			BsonDocument bd = b["$timestamp"].AsBsonDocument;
			Assert.IsNotNull(bd);

			Assert.AreEqual(a.Timestamp, (int)Math.Floor(bd["t"].AsDouble));
			//Assert.AreEqual(a.Value, bd["i"].AsInt32);

			//const int TIMESTAMP_ACCURACY = 12;
			//Assert.AreEqual(a.ToString().Remove(TIMESTAMP_ACCURACY), v.AsDouble.ToString(DBL_ROUND_LIMIT).Remove(TIMESTAMP_ACCURACY));
		}

		public static void AreEqual(this BsonDocument a, BsonDocument b, bool isgnoreCount = false)
		{
			if (a == null)
				Assert.IsNull(b);
			else
			{
				Assert.IsNotNull(b);
				if(!isgnoreCount)
					Assert.AreEqual(a.ElementCount, b.ElementCount);

				foreach (BsonElement el in a)
				{
					Assert.IsTrue(b.Contains(el.Name), "b[{0}] does not exists", el.Name);
					el.Value.AreEqual(b[el.Name]);
				}
			}
		}
	}
}
