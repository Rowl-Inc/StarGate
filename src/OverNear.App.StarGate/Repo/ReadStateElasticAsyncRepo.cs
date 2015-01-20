using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.IO;
using System.Xml.Serialization;

using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	[Serializable]
	public class ReadStateElasticAsyncRepo : ReadStateElasticRepo, IDisposable
	{
		enum RsActionType { Update, Create }
		class RsAction
		{
			public BsonTimestamp TS { get; set; }
			public RsActionType Act { get; set; }
		}

		static readonly ConcurrentDictionary<string, RsAction> _writes = new ConcurrentDictionary<string, RsAction>();

		readonly Thread _writeThread;
		public ReadStateElasticAsyncRepo()
		{
			_writeThread = new Thread(CheckThenWrite) { IsBackground = true, Name = "StEsAsc" };
		}
		~ReadStateElasticAsyncRepo() { Dispose(); }

		public VerboseLogLevel VerboseLog { get; set; }

		int _disposed = 0;
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				try
				{
					if (_writeThread != null && _writeThread.IsAlive)
					{
						if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
							_logger.Debug("Dispose thread cleanup begin");

						_writeThread.Join();
						if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
							_logger.Debug("Dispose thread cleanup done");
					}
				}
				catch (Exception ex)
				{
					_logger.Error("Dispose", ex);
				}
			}
		}

		int _threadStarted = 0;

		public override bool Create(ReadState state)
		{
			if (state == null)
				throw new ArgumentNullException("state");

			if (Interlocked.CompareExchange(ref _threadStarted, 1, 0) == 0)
				_writeThread.Start();

			_writes.AddOrUpdate(state.Id, new RsAction { TS = state.TimeStamp, Act = RsActionType.Create });
			return true;
		}

		public override bool UpdateTimeStamp(string id, BsonTimestamp newTimeStamp, BsonTimestamp lastTimeStamp = null)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("id can not be null or blank");
			if (newTimeStamp == null)
				throw new ArgumentNullException("newTimeStamp");
			if (lastTimeStamp != null && lastTimeStamp > newTimeStamp)
				throw new ArgumentOutOfRangeException("lastTimeStamp can not be larger than newTimeStamp");

			if (Interlocked.CompareExchange(ref _threadStarted, 1, 0) == 0)
				_writeThread.Start();

			var va = new RsAction { TS = newTimeStamp, Act = RsActionType.Update };
			_writes.AddOrUpdate(id, va, (k, v) => v.TS.Value > va.TS.Value ? v : va);
			return true;
		}

		const int INNER_SLEEP = 100;
		[XmlIgnore]
		int _innerMS = INNER_SLEEP;
		[XmlAttribute]
		public int InnerLoopSleepMs
		{
			get { return _innerMS; }
			set { _innerMS = value == 0 ? INNER_SLEEP : value; }
		}

		const int OUTER_SLEEP = 250;
		[XmlIgnore]
		int _outerMS = OUTER_SLEEP;
		[XmlAttribute]
		public int OuterLoopSleepMs
		{
			get { return _outerMS; }
			set { _outerMS = value == 0 ? OUTER_SLEEP : value; }
		}

		void CheckThenWrite()
		{
			try
			{
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.InfoFormat("CheckThenWrite THREAD begin");

				do
				{
					try
					{
						while (_writes.Count > 0)
						{
							string k = _writes.Keys.FirstOrDefault();
							RsAction ra;
							if (!string.IsNullOrWhiteSpace(k) && _writes.TryRemove(k, out ra))
							{
								try
								{
									switch (ra.Act)
									{
										case RsActionType.Create:
											base.Create(new ReadState { Id = k, TimeStamp = ra.TS, });
											break;
										case RsActionType.Update:
										default:
											base.UpdateTimeStamp(k, ra.TS);
											break;
									}
								}
								catch (Exception lex)
								{
									_logger.WarnFormat("CheckThenWrite INNER LOOP", lex);
								}
							}
							Thread.Sleep(InnerLoopSleepMs);
						}
					}
					catch (Exception ex)
					{
						_logger.Error("CheckThenWrite", ex);
					}
					finally
					{
						Thread.Sleep(OuterLoopSleepMs);
					}
				} while (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0); //not disposed
				if (VerboseLog.HasFlag(VerboseLogLevel.DbConnection))
					_logger.InfoFormat("CheckThenWrite THREAD exit");
			}
			catch (Exception tex)
			{
				_logger.Error("CheckThenWrite THREAD", tex);
			}
		}

	}
}
