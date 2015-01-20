using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

using NUnit.Framework;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Repo;

namespace OverNear.App.StarGate.Tests
{
	[Explicit]
	[TestFixture, Category("Default")]
	public class Online_ReadStateRepoTests : IDisposable
	{
		readonly HashSet<string> _rsIds = new HashSet<string>();
		readonly ReadStateMongoRepo _repo = new ReadStateMongoRepo();

		ReadState Mock()
		{
			ReadState r;
			bool addOk;
			do
			{
				string id = "ORRT-" + DateTime.UtcNow.Ticks;
				var ts = new BsonTimestamp((int)DateTime.UtcNow.ToUnixTime(), 1);
				r = new ReadState { Id = id, TimeStamp = ts };
				addOk = _rsIds.Add(r.Id);
				Thread.Sleep(1100);
			}
			while (!addOk);
			return r;
		}

		public Online_ReadStateRepoTests()
		{
		}

		~Online_ReadStateRepoTests() { Dispose(); }

		int _disposed = 0;
		[TestFixtureTearDown]
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				if (!_rsIds.IsNullOrEmpty())
					_rsIds.ForEach(_repo.Clear);
			}
		}

		[Test]
		public void ReadState_CRUD()
		{
			ReadState og = Mock();
			Assert.IsNotNull(og);
			{
				ReadState missing = _repo.Load(og.Id);
				Assert.IsNull(missing);
			}
			Assert.IsTrue(_repo.Create(og));
			Assert.IsFalse(_repo.Create(og));

			RefetchCheck(og);

			og.TimeStamp = new BsonTimestamp(og.TimeStamp.Value + 1);
			_repo.UpdateTimeStamp(og.Id, og.TimeStamp);

			RefetchCheck(og);

			bool ok = _repo.UpdateTimeStamp(og.Id, new BsonTimestamp(og.TimeStamp.Value + 1));
			Assert.IsTrue(ok);

			og.TimeStamp = new BsonTimestamp(og.TimeStamp.Value + 1);
			RefetchCheck(og);

			_repo.Clear(og.Id);
			ReadState fr = _repo.Load(og.Id);
			Assert.IsNull(fr);
		}

		[Test]
		public void BadTimeStampUpdates()
		{
			ReadState og = Mock();
			Assert.IsNotNull(og);
			Assert.IsTrue(_repo.Create(og));
			RefetchCheck(og);

			Assert.IsFalse(_repo.Create(new ReadState { Id = og.Id, TimeStamp = new BsonTimestamp(og.TimeStamp.Value - 100) }));
			RefetchCheck(og);

			var lessThanLast = new BsonTimestamp(og.TimeStamp.Value - 1);
			Assert.Throws<ArgumentOutOfRangeException>(() => _repo.UpdateTimeStamp(og.Id, lessThanLast, og.TimeStamp));
			RefetchCheck(og);

			Assert.IsFalse(_repo.UpdateTimeStamp(og.Id, og.TimeStamp));
			RefetchCheck(og);

			Assert.IsFalse(_repo.UpdateTimeStamp(og.Id, lessThanLast));
			RefetchCheck(og);

			og.TimeStamp = new BsonTimestamp(og.TimeStamp.Value + 1); //bump
			Assert.IsTrue(_repo.UpdateTimeStamp(og.Id, og.TimeStamp));
			RefetchCheck(og);

			BsonTimestamp last = og.TimeStamp;
			og.TimeStamp = new BsonTimestamp(og.TimeStamp.Value + 1); //bump
			Assert.IsTrue(_repo.UpdateTimeStamp(og.Id, og.TimeStamp, last));
			RefetchCheck(og);

			Assert.IsFalse(_repo.UpdateTimeStamp(og.Id, og.TimeStamp, last));
			RefetchCheck(og);

			og.TimeStamp = new BsonTimestamp(og.TimeStamp.Value + 1); //bump
			Assert.IsFalse(_repo.UpdateTimeStamp(og.Id, og.TimeStamp, last));
		}

		ReadState RefetchCheck(ReadState og)
		{
			Assert.IsNotNull(og);
			Assert.IsFalse(string.IsNullOrWhiteSpace(og.Id), "id is null or blank");

			ReadState re = _repo.Load(og.Id);
			Assert.IsNotNull(re);
			AreEqual(og, re);
			return re;
		}

		void AreEqual(ReadState a, ReadState b)
		{
			if (a == null)
				Assert.IsNull(b);
			else
			{
				Assert.IsNotNull(b);
				Assert.AreEqual(a.Id, b.Id);
				Assert.AreEqual(a.TimeStamp, b.TimeStamp);
			}
		}
	}
}
