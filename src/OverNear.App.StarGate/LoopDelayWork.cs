using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	class LoopDelayWork : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		
		readonly IWorkUnit _work;
		readonly TimeSpan _sleep;

		public LoopDelayWork(IWorkUnit work, TimeSpan sleep)
		{
			if (work == null)
				throw new ArgumentNullException("work");
			if (sleep <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException("sleep can not be <= 0");

			_work = work;
			_sleep = sleep;
			VerboseLog = work.VerboseLog;
		}

		public VerboseLogLevel VerboseLog { get; set; }

		long _iterations = 0;

		public void Run()
		{
			if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
				_logger.Debug("Run starting");

			while (true)
			{
				long c = Interlocked.Increment(ref _iterations);
				if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
					_logger.DebugFormat("Begin Run #{0}", c);

				_work.Run();

				_logger.InfoFormat("Completed Run #{0}. Pausing for {1}", c, _sleep);

				Thread.Sleep(_sleep);
				lock (_dlock)
				{
					if (_dispoed)
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
							_logger.Info("Dispose trigger Run exit");
						break;
					}
				}
			}

			if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
				_logger.Debug("Run exit");
		}

		readonly object _dlock = new object();
		bool _dispoed = false;

		public void Dispose()
		{
			lock (_dlock)
			{
				if (!_dispoed)
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.Debug("Dispose triggered");
					_dispoed = true;
					_work.Dispose();
				}
			}
		}

	}
}
