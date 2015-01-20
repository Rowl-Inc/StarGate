using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	class WorkThread : IDisposable
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		readonly Thread _t;
		readonly IWorkUnit _wu;

		public WorkThread(IWorkUnit work)
		{
			if (work == null)
				throw new ArgumentNullException("work");

			_wu = work;
			VerboseLog = work.VerboseLog;

			if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
				_logger.DebugFormat("CTOR: Logic thread setup -> {0}", _wu);

			string tn;
			_t = new Thread(_wu.Run) 
			{ 
				IsBackground = true, 
				Name = tn = "WT" + _wu.GetHashCode(),
				//ExecutionContext = AppDomain.CurrentDomain.ex
				//ApartmentState = ApartmentState.MTA,
			};

			if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
				_logger.InfoFormat("CTOR: Logic thread {0} starting", tn);
			_t.Start();
			if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
				_logger.Debug("CTOR: done");
		}

		public VerboseLogLevel VerboseLog { get; set; }

		~WorkThread() { Dispose(); }
		int _disposed = 0;
		public void Dispose()
		{
			try
			{
				if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.Debug("Dispose: Killing logic");

					if (_wu != null)
						_wu.Dispose();
					if (_t != null && _t.IsAlive)
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
							_logger.Debug("Dispose: Killing thread");

						_t.Join(1000);
					}

					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.Info("Dispose: All done");
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Dispose", ex);
			}
		}
	}
}
