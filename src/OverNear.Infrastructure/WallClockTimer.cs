using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Configuration;
namespace OverNear.Infrastructure
{
    /// <summary>
    /// Use this to capture detail processing time for a method using wall clock datetime
    /// </summary>
    public class WallClockTimer : IDisposable
    {
        readonly Dictionary<string, TimeSpan> _captures = new Dictionary<string, TimeSpan>();
        readonly string _name;
        public string Name { get { return _name; } }

        long _timerStart = 0;
        readonly Action<string> _slowlogMethod;

        /// <summary>
        /// Name of this process timer for logging sake
        /// </summary>
        /// <param name="name"></param>
        public WallClockTimer(string name, Action<string> slowlogMethod = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name can not be null or empty");

            Interlocked.Exchange(ref _timerStart, DateTime.UtcNow.Ticks);
            _name = name;
            _slowlogMethod = slowlogMethod;
        }

        long _totalMS = 0;
        long _capstart = 0;
        public void Mark()
        {
            Interlocked.Exchange(ref _capstart, DateTime.UtcNow.Ticks);
        }
        public TimeSpan Capture(string name)
        {
            DateTime started = new DateTime(Interlocked.Read(ref _capstart), DateTimeKind.Utc);
            TimeSpan took = DateTime.UtcNow.Subtract(started);
            lock (_captures)
            {
                if (_captures.ContainsKey(name))
                    _captures[name] += took;
                else
                    _captures.Add(name, took);
            }
            return took;
        }
        public TimeSpan AddTime(string name, TimeSpan value)
        {
            TimeSpan total;
            lock (_captures)
            {
                if (_captures.ContainsKey(name))
                    _captures[name] = total = _captures[name] + value;
                else
                    _captures.Add(name, total = value);
            }
            return total;
        }

        int _stopped = 0;
        public TimeSpan Stop()
        {
            if (Interlocked.CompareExchange(ref _stopped, 1, 0) == 0)
            {
                DateTime started = new DateTime(Interlocked.Read(ref _timerStart), DateTimeKind.Utc);
                TimeSpan took = DateTime.UtcNow.Subtract(started);
                long totalMS = Interlocked.Add(ref _totalMS, (long)took.TotalMilliseconds);
                return TimeSpan.FromMilliseconds(totalMS);
            }
            else
                return Elapsed;
        }

        public void Resume()
        {
            if (Interlocked.CompareExchange(ref _stopped, 0, 1) == 1)
                Interlocked.Exchange(ref _timerStart, DateTime.UtcNow.Ticks);
        }

        public TimeSpan Elapsed { get { return TimeSpan.FromMilliseconds(Interlocked.Read(ref _totalMS)); } }
        public TimeSpan this[string name]
        {
            get { lock (_captures) return !string.IsNullOrEmpty(name) && _captures.ContainsKey(name) ? _captures[name] : TimeSpan.Zero; }
        }
        public IEnumerable<string> Keys { get { lock (_captures) return _captures.Keys; } }
        public int Count { get { lock (_captures) return _captures.Count; } }

        public override string ToString()
        {
            var sb = new StringBuilder("ProcessTimer [");
            sb.Append(_name);
            sb.AppendFormat("] @{0} ", Elapsed);
            lock (_captures)
            {
                foreach (var p in _captures)
                    sb.AppendFormat("{0}={1} ", p.Key, p.Value);
            }
            return sb.ToString();
        }

		///// <summary>
		///// Will stop timer
		///// </summary>
		//public void LogSlow(Action<string> logMethod)
		//{
		//	LogSlow(logMethod, SystemSettings.Instance.SlowLog);
		//}

        /// <summary>
        /// Will stop timer
        /// </summary>
        public void LogSlow(Action<string> logMethod, TimeSpan threshold)
        {
            if (this.Stop() > threshold && logMethod != null)
                logMethod("|_SLOWOP| " + this.ToString());
        }

        public void Log(Action<string> logMethod)
        {
            if (logMethod != null)
                logMethod(this.ToString());
        }
        public void Log(Action<string> logMethod, Action<string> logSlowMethod, TimeSpan threshold)
        {
            if (logMethod != null)
            {
                if (logSlowMethod != null && this.Stop() > threshold)
                    logSlowMethod(this.ToString());
                else
                    logMethod(this.ToString());
            }
        }

        int _disposed = 0;
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                LogSlow(_slowlogMethod, TimeSpan.FromSeconds(3));
        }
    }
}
