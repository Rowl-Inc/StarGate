using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Configuration;

namespace OverNear.Infrastructure
{
    /// <summary>
    /// Use this to capture detail processing time for a method using System.Diagnostic.Timer
    /// </summary>
    public class StopWatchTimer
    {
        readonly Dictionary<string, TimeSpan> _captures = new Dictionary<string, TimeSpan>();
        readonly Stopwatch _w;
        readonly string _name;
        public string Name { get { return _name; } }

        /// <summary>
        /// Name of this process timer for logging sake
        /// </summary>
        /// <param name="name"></param>
        public StopWatchTimer(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name can not be null or empty");

            _name = name;

            _w = new Stopwatch();
            _w.Start();
        }

        double _capstart;
        public void Mark()
        {
            lock (_w)
            {
                _w.Stop();
                _capstart = _w.Elapsed.TotalMilliseconds;
                _w.Start();
            }
        }
        public TimeSpan Capture(string name)
        {
            TimeSpan took;
            lock (_w)
            {
                _w.Stop();
                took = TimeSpan.FromMilliseconds(_w.Elapsed.TotalMilliseconds - _capstart);
                if (_captures.ContainsKey(name))
                    _captures[name] += took;
                else
                    _captures.Add(name, took);
                _w.Start();
            }
            return took;
        }
        public TimeSpan AddTime(string name, TimeSpan value)
        {
            TimeSpan total;
            lock (_w)
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
                lock (_w)
                    _w.Stop();
            }
            return _w.Elapsed;
        }

        public void Resume()
        {
            if (Interlocked.CompareExchange(ref _stopped, 0, 1) == 1)
            {
                lock (_w)
                    _w.Start();
            }
        }

        public TimeSpan Elapsed { get { lock (_w) return _w.Elapsed; } }
        public TimeSpan this[string name]
        {
            get { lock (_w) return !string.IsNullOrEmpty(name) && _captures.ContainsKey(name) ? _captures[name] : TimeSpan.Zero; }
        }
        public IEnumerable<string> Keys { get { lock (_w) return _captures.Keys; } }
        public int Count { get { lock (_w) return _captures.Count; } }

        public override string ToString()
        {
            var sb = new StringBuilder("ProcessTimer [");
            sb.Append(_name);
            lock (_w)
            {
                sb.AppendFormat("] @{0} ", _w.Elapsed);
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
    }
}
