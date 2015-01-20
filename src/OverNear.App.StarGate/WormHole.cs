using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Configuration;

using MongoDB.Bson;

using log4net;
using OverNear.Infrastructure;

using OverNear.App.StarGate.Subscribe;
using OverNear.App.StarGate.Repo;

namespace OverNear.App.StarGate
{
	public sealed class WormHole : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		long _state = -1; //default

		readonly string _replicaName;
		readonly string _configuredDb;
		readonly RoutingChain _chainOfResponsibilities;
		readonly Func<IOpLogReader> _getReader;
		readonly IReadStateRepo _readStateRepo;

		public VerboseLogLevel VerboseLog { get; set; }

		public WormHole(
			string replicaName,
			string configuredDb,
			RouteList rl, 
			Func<IOpLogReader> getReader, 
			IReadStateRepo readStateRepo,
			string basePath = null)
		{
			if (string.IsNullOrWhiteSpace(replicaName))
				throw new ArgumentException("replicaName can not be null or empty!");
			if (string.IsNullOrWhiteSpace(configuredDb))
				throw new ArgumentException("configuredDb can not be null or empty!");

			if (rl == null)
				throw new ArgumentNullException("rl");
			if (getReader == null)
				throw new ArgumentNullException("getReader");
			if (readStateRepo == null)
				throw new ArgumentNullException("readStateRepo");

			_configuredDb = configuredDb;
			_replicaName = replicaName;
			_getReader = getReader;
			_readStateRepo = readStateRepo;

			_chainOfResponsibilities = new RoutingChain(rl) { BasePath = basePath };
			Interlocked.Exchange(ref _state, 0); //ctor success!

			if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
				_logger.Info("CTOR init ok");
		}

		IOpLogReader _reader;
		readonly object _lastLock = new object();
		BsonTimestamp _lastTs = new BsonTimestamp(0);
		BsonTimestamp LastReadTs
		{
			get { lock (_lastLock) return _lastTs; }
			set { lock (_lastLock) _lastTs = value; }
		}

		/// <summary>
		/// Call to open WormHole. Note, this method will block until Close(...) is called from another thread!
		/// </summary>
		public void Run()
		{
			if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
			{
				DateTime started = DateTime.UtcNow;
				try
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Info("Run: Init OpLogRead");

					Repo.ReadState rs = _readStateRepo.Load(_replicaName); //load state from db here...

					using (_reader = _getReader())
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
							_logger.Debug("Run: Begin OpLogRead");

						BsonTimestamp cts = rs != null ? rs.TimeStamp : new BsonTimestamp(0);
						_reader.Read(ref cts, o => EvalNextOplog(o, _reader.CurrentRepo)); //thread blocking...
					}
					if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Info("Run: OpLogRead Exit OK");
				}
				catch (Exception ex)
				{
					_logger.Error("Run Exploded", ex);
					throw;
				}
				finally
				{
					if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
					{
						DateTime n = DateTime.UtcNow;
						_logger.DebugFormat("Run: exit method at {0} took {1}", n, n - started);
					}
				}
			}
			else
				_logger.Warn("Run: Already Open!");
		}

		void EvalNextOplog(OpLogLine op, IMongoRepo repo)
		{
			var context = new OpLogContext(op, repo, _configuredDb, _chainOfResponsibilities)
			{
				VerboseLog = this.VerboseLog,
			};
			_chainOfResponsibilities.Evaluate(context); //pass in the oplog to cor to eval
			if (LastReadTs < op.TimeStamp)
			{
				if (!_readStateRepo.UpdateTimeStamp(_replicaName, op.TimeStamp, LastReadTs))
					_logger.WarnFormat("Unable update last ts to {0} for {1}", op.TimeStamp.ToUTC(), op);

				LastReadTs = new BsonTimestamp(op.TimeStamp.Value);
			}
		}

		/// <summary>
		/// Call to close WormHole and release blocking on Open(...) in another thread
		/// </summary>
		/// <param name="forever">if true, this WormHole handle can never be open after</param>
		void Close(bool forever = false)
		{
			if (Interlocked.CompareExchange(ref _state, forever ? 2 : 0, 1) == 1)
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
					_logger.Info("Close: Attempt Request");

				if (_reader != null)
					_reader.Dispose();

				try
				{
					if (_readStateRepo != null && _readStateRepo is IDisposable)
						(_readStateRepo as IDisposable).Dispose();
				}
				catch (Exception ex)
				{
					_logger.Error("Close(" + forever + ")", ex);
				}
			}
			else
				_logger.Warn("Already Closed!");
		}
		~WormHole() { Dispose(); }
		public void Dispose()
		{
			try
			{
				Close(true);
			}
			catch (Exception ex)
			{
				_logger.Error("Dispose", ex);
			}
		}

		public override string ToString()
		{
			return string.Format("{0} [_getReader:{1}]", 
				this.GetType().Name, 
				_getReader != null ? _getReader.Method.Name : "<null>");
		}
	}
}
