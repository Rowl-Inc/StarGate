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
	public sealed class BigBang : IWorkUnit
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		readonly string _replicaName;
		readonly string _configuredDb;
		readonly RoutingChain _chainOfResponsibilities;
		readonly Func<IOpLogReader> _getReader;
		readonly IReadStateRepo _readStateRepo;

		public VerboseLogLevel VerboseLog { get; set; }

		public BigBang(
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
			if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
				_logger.Info("CTOR init ok");
		}

		~BigBang() { Dispose(); }
		int _disposed = 0;
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				try
				{
					_logger.Debug("Dispose: Triggered");
					if (_reader != null)
						_reader.Dispose(); //force reader dispose...

					_logger.Info("Dispose: Completed");
				}
				catch (Exception ex)
				{
					_logger.Error("Dispose", ex);
				}
			}
			else
				_logger.Warn("Dispose: Already disposed");
		}

		readonly object _clearlock = new object();
		bool _clear;
		public bool ClearState
		{
			get { lock (_clearlock) { return _clear; } }
			set { lock (_clearlock) { _clear = value; } }
		}

		IOpLogReader _reader;
		int _started = 0;
		public void Run()
		{
			if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
			{
				if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
				{
					var started = DateTime.UtcNow;
					try
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
							_logger.Info("Run: Begin");

						var currentTs = new BsonTimestamp(0);
						if (ClearState)
						{
							_readStateRepo.Clear(_replicaName);
							SetLastOplogTsState(currentTs, true); //expects the lastTs value to be that of the true final opLog value
						}

						if (CanBootStrap())
						{
							try
							{
								using (_reader = _getReader())
								{
									if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
										_logger.Debug("Run: Begin OpLogRead");
									_reader.Read(ref currentTs, (op) =>
									{
										var context = new OpLogContext(op, _reader.CurrentRepo, _configuredDb, _chainOfResponsibilities)
										{
											VerboseLog = this.VerboseLog,
										};
										_chainOfResponsibilities.Evaluate(context); //pass in the oplog to cor to eval
									}); //thread blocking...
								}

							}
							finally
							{
								SetLastOplogTsState(currentTs, false); //expects the lastTs value to be that of the true final opLog value
								if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
									_logger.Info("Run: Ends");
							}
						}
					}
					catch (Exception ex)
					{
						_logger.Error("Run:", ex);
						throw;
					}
					finally
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
							_logger.InfoFormat("Run: Exit function, took: {0}", DateTime.UtcNow - started);
					}
				}
				else
					_logger.Warn("Run: Already started!");
			}
			else
				_logger.Warn("Run: Can not start, already disposed!");
		}

		bool CanBootStrap()
		{
			ReadState rs = _readStateRepo.Load(_replicaName);
			bool ok = rs == null || rs.TimeStamp.Value <= 0;
			if (ok)
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
					_logger.InfoFormat("BigBang CanBootStrap, and will do now for {0}", _replicaName);
			}
			else
				_logger.WarnFormat("BigBang can not CanBootStrap, already have an existing ReadState: {0}", rs.ToJson());
			return ok;
		}

		void SetLastOplogTsState(BsonTimestamp lastTs, bool upsert)
		{
			if (lastTs.Value == 0)
			{
				//throw new ApplicationException("lastTs.value is 0!");
				_logger.WarnFormat("SetLastOplogTsState(.., {0}) lastTs.value is 0!", upsert);
				return;
			}

			var sb = new StringBuilder(); //error message
			try
			{
				if (!upsert || _readStateRepo.Load(_replicaName) == null)
				{
					_readStateRepo.Create(new ReadState
					{
						Id = _replicaName,
						TimeStamp = lastTs,
					});
					if (VerboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.DebugFormat("SetLastOplogTsState saved last ts value {0} for {1}", lastTs.ToUTC(), _replicaName);
				}
				else
					_logger.WarnFormat("SetLastOplogTsState not storing last ts value {0} for {1}", lastTs.ToUTC(), _replicaName);
			}
			catch (Exception ex)
			{
				_logger.Error("SetLastOplogTsState: " + _replicaName, ex);

				sb.Append("SetLastOplogTsState: ");
				sb.Append(_replicaName);
				sb.Append("\r\n");
				sb.Append(ex.ToString());
				sb.AppendLine("\r\n-------------------------");
			}
			if (sb.Length > 0)
				throw new ApplicationException(sb.ToString());
		}
	}
}
