using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	class NoExceptionWork : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		readonly IWorkUnit _work;
		readonly ManualResetEvent _reset;

		public NoExceptionWork(IWorkUnit work, ManualResetEvent reset = null)
		{
			if (work == null)
				throw new ArgumentNullException("work");

			_work = work;
			_reset = reset;
			VerboseLog = work.VerboseLog;
		}

		public VerboseLogLevel VerboseLog { get; set; }

		public void Run()
		{
			try
			{
				_work.Run();
				if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
					_logger.Debug("Run completed successfully.");
			}
			catch //(Exception ex)
			{
				_logger.Warn("Run exception. Exiting Run");
			}
			finally
			{
				if (_reset != null)
				{
					_reset.Set();
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.Debug("Run triggered manual reset");
				}
			}
		}

		public void Dispose()
		{
			if (_work != null)
				_work.Dispose();
		}
	}
}
