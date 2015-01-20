using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using MongoDB.Bson;
using OverNear.Infrastructure;
using OverNear.App.StarGate.Subscribe;

namespace OverNear.App.StarGate.Tests
{
	class DummyPublisher : Trigger
	{
		public event Action<DummyPublisher, IContext> OnPublish;

		public int PublishCount { get; private set; }
		public IContext LastContext { get; private set; }
		public DateTime LastPublished { get; private set; }

		public override void Execute(IContext context)
		{
			try
			{
				PublishCount++;
				LastContext = context;
#if DEBUG
				string json;
				if (context != null && context.Payload != null)
					json = context.Payload.ToJson();
				else
					json = "<null>";

				Console.WriteLine("DummyPublisher: {0}", json);
#endif
				Thread.Sleep(25); //simulate publish lag...
				if (OnPublish != null)
					OnPublish(this, context);
			}
			finally
			{
				LastPublished = DateTime.UtcNow;
			}
		}
	}
}
