using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	public sealed class TimeLineSequence : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		readonly IWorkUnit[] _events;

		public TimeLineSequence(IWorkUnit[] events)
		{
			if (events.IsNullOrEmpty())
				throw new ArgumentException("events can not be null or empty");
			if (events.Any(ev => ev == null))
				throw new ArgumentException("events can not contains null item");

			_events = events;
			_logger.Debug("CTOR ok");
		}

		public VerboseLogLevel VerboseLog { get; set; }

		~TimeLineSequence() { Dispose(); }
		int _disposed = 0;
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				_logger.Debug("Dispose: triggered");
				foreach (IWorkUnit w in _events)
				{
					try
					{
						if (w != null)
							w.Dispose();
					}
					catch (Exception ex)
					{
						_logger.Error("Dispose: " + w.ToString(), ex);
					}
				}
				_logger.Info("Dispose: completed");
			}
			else
				_logger.Warn("Dispose: already happened");
		}

		int _ranOnce = 0;
		public void Run()
		{
			if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
			{
				if (Interlocked.CompareExchange(ref _ranOnce, 1, 0) == 0)
				{
					for(int i=0; i<_events.Length; i++)
					{
						int n = i + 1;
						DateTime started = DateTime.UtcNow;
						try
						{
							_logger.DebugFormat("Run: begin sequence {0} of {1} | {2}", n, _events.Length, _events[i]);
							_events[i].Run();
							_logger.InfoFormat("Run: completed sequene {0} of {1} | {2}", n, _events.Length, _events[i]);
						}
						catch (Exception ex)
						{
							string msg = string.Format("Run: error during sequence {0} of {1}", n, _events.Length);
							_logger.Error(msg, ex);
							throw;
						}
						finally
						{
							_logger.DebugFormat("Run sequence {0}/{1} took {2} | {3}", n, _events.Length, DateTime.UtcNow - started, _events[i]);
						}
					}
				}
				else
					_logger.Warn("Run: already ran once");
			}
			else
				_logger.Warn("Run: already disposed!");
		}

	}
}
