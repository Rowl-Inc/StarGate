using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	public interface IContext : ICloneable
	{
		OpLogLine Original { get; }
		BsonValue Payload { get; set; }
		IMongoRepo CurrentRepo { get; }
		VerboseLogLevel VerboseLog { get; }

		string ConfiguredDb { get; }

		IContext Copy();
		IResponsibilityChain TaskChain { get; }
	}
}
