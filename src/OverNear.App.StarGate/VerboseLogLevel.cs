using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate
{
	[Flags]
	public enum VerboseLogLevel : int
	{
		None = 0,
		DbConnection = 1,
		ThreadInfo = 2,
		Request = 4,
		Response = 8,
		Routing = 16,
		ServiceLogic = 32,

		Default = None,
		All = DbConnection | ThreadInfo | Request | Response | Routing | ServiceLogic,
		IO = DbConnection | Request | Response,
	}
}
