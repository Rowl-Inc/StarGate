using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	/// <summary>
	/// Wait for some external trigger indefinately before starting task
	/// </summary>
	class WaitForTriggerWork : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		readonly ManualResetEvent[] _startTriggers;
		readonly IWorkUnit _work;

		public WaitForTriggerWork(IWorkUnit work, params ManualResetEvent[] startTriggers)
		{
			if (work == null)
				throw new ArgumentNullException("work");

			_work = work;
			VerboseLog = work.VerboseLog;

			if (!startTriggers.IsNullOrEmpty())
				startTriggers = (from s in startTriggers where s != null select s).ToArray();

			_startTriggers = startTriggers ?? new ManualResetEvent[0];
			//if(_startTriggers.IsNullOrEmpty())
			//	throw new ArgumentException("startTriggers can not be null or empty");
		}

		public VerboseLogLevel VerboseLog { get; set; }

		enum RunState : int
		{
			NotStarted = 0,
			Waiting = 1,
			Executing = 2,
			Disposed = 3,
		}
		/// <summary>
		/// Maps to RunState to ensure things does not happen out of sequence
		/// A poorman's state machine
		/// </summary>
		int _state = 0;

		public void Run()
		{
			try
			{
				if(VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
					_logger.Debug("Run triggered");

				int state;
				if ((state = Interlocked.CompareExchange(ref _state, 1, 0)) == 0) //one way street, only start if it never runs before
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.DebugFormat("Run Waiting for {0} trigger handles {1}", _startTriggers.Length, (RunState)state);

					if (!_startTriggers.IsNullOrEmpty())
						WaitHandle.WaitAll(_startTriggers);

					if ((state = Interlocked.CompareExchange(ref _state, 2, 1)) == 1) //only continue if state is not disposed: 3
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
							_logger.InfoFormat("Run got all resume trigger, will attempt to start actual work! {0}", (RunState)state);

						_work.Run();
					}
					else
						_logger.WarnFormat("Run got all resume trigger, but dispose already triggered! {0}", (RunState)state);
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Run", ex);
				throw;
			}
		}

		public void Dispose()
		{
			try
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
					_logger.Debug("Dispose triggered");

				int state;
				if ((state = Interlocked.CompareExchange(ref _state, 3, 1)) == 1) //waiting
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.InfoFormat("Dispose force manual reset: {0}", (RunState)state);
					if (!_startTriggers.IsNullOrEmpty())
						_startTriggers.ForEach(t => t.Reset()); //force manual reset for an exist
				}
				else if ((state = Interlocked.CompareExchange(ref _state, 3, 2)) == 2) //executing...
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ThreadInfo))
						_logger.InfoFormat("Dispose cleanup logic: {0}", (RunState)state);
					if (_work != null)
						_work.Dispose(); //force job dispose
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Dispose", ex);
				throw;
			}
		}
	}
}
