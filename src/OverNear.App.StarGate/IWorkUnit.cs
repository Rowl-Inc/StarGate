using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate
{
	public interface IWorkUnit : IDisposable
	{
		void Run();

		VerboseLogLevel VerboseLog { get; }
	}
}
