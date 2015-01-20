using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Chain of task responsibility:
	/// The context is passed among each task in the chain.
	/// When a task determine that it needs to do work on the context, it will.
	/// Once the work is done, it has the option of telling the evaluator to stop 
	/// </summary>
	public class RoutingChain : CollectionTrigger<Route>, IResponsibilityChain
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public RoutingChain(IEnumerable<Route> tasks = null)
			: base(new List<Route>(tasks ?? new Route[0]))
		{
			this.OnBeforeAdd += FrozenCheck;
			this.OnAfterAdd += ValidateRoute;
			this.OnBeforeRemove += FrozenCheck;
			this.OnBeforeClear += FrozenCheck;
		}

		void ValidateRoute(ICollection<Route> self, Route task)
		{
			if (task == null)
				throw new ArgumentNullException("Route task can not be null!");
			if (task.Trigger == null)
				throw new ArgumentException("Route task.Trigger can not be null!");
		}

		void FrozenCheck(ICollection<Route> self, Route task) { FrozenCheck(self); }
		void FrozenCheck(ICollection<Route> self)
		{
			if (IsFrozen)
				throw new InvalidOperationException("Operation not allowed once Route logic is Frozen");
		}

		volatile bool _isFrozen = false;
		public bool IsFrozen { get { lock (_padlock) { return _isFrozen; } } }

		string _basePath = string.Empty;
		/// <summary>
		/// Optional base path. If exists and actual route path is relative, will add this portion in
		/// </summary>
		public string BasePath
		{
			get { return _basePath; }
			set { _basePath = value.TrimToEmpty(); }
		}

		public Uri GetAbsUrl(string path)
		{
			Uri u = null;
			if (!string.IsNullOrWhiteSpace(path))
			{
				if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
				{
					u = new Uri(path);
					if (string.IsNullOrWhiteSpace(u.DnsSafeHost))
						u = null;
				}
				if(u == null)
				{
					if (path.First() == '/' && !string.IsNullOrEmpty(BasePath) && BasePath.Last() == '/')
						path = path.Substring(1);

					u = new Uri(BasePath + path);
				}
			}
			return u;
		}

		public void Evaluate(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			lock (_padlock) { _isFrozen = true; } //share base padlock

			foreach (Route task in base._collection) //bypass all locks by using low level
			{
				try
				{
					if (task.Trigger == null)
						throw new InvalidOperationException("Trigger can not be null!");

					TaskProcessState st = task.Evaluate(context);
					if (st == TaskProcessState.Return)
					{
						if (!task.Continue)
						{
							if(context.VerboseLog.HasFlag(VerboseLogLevel.Response))
								_logger.DebugFormat("Task {0} returns for {1}", task, context);
							break;
						}
						//else if (context.VerboseLog.HasFlag(VerboseLogLevel.Response))
						//	_logger.DebugFormat("Task {0} Evaluate success but Continues on for {1}", task, context);
					}
				}
				catch (Exception ex)
				{
					_logger.Error("Evaluate", ex);
				}
			}
		}

		/// <summary>
		/// Reset all singleton states in the chain (will cause the route chain to be frozen)
		/// </summary>
		public virtual void Reset()
		{
			lock (_padlock) { _isFrozen = true; } //share base padlock

			foreach (Route task in base._collection) //bypass all locks by using low level
			{
				try
				{
					task.Reset();
				}
				catch (Exception ex)
				{
					_logger.Error("Reset", ex);
				}
			}
		}

	}
}
