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

using MongoDB.Bson;
using MongoDB.Driver;
using log4net;

using OverNear.Infrastructure;

using OverNear.App.StarGate.Repo;

namespace OverNear.App.StarGate
{
	public partial class ServiceContainer : ServiceBase
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");
		readonly CollectionTrigger<WorkThread> _jobs = new CollectionTrigger<WorkThread>(new List<WorkThread>());

		readonly VerboseLogLevel _verboseLog = VerboseLogLevel.Default;
		readonly bool _hostedDb = false;

		public ServiceContainer()
		{
			InitializeComponent();
			_verboseLog = ConfigurationManager.AppSettings.ExtractConfiguration("_verboseLog", _verboseLog);
			_hostedDb = ConfigurationManager.AppSettings.ExtractConfiguration("_hostedDb", _hostedDb);

			_jobs.OnBeforeAdd += FrozenCheck;
			_jobs.OnBeforeRemove += FrozenCheck;
			_jobs.OnBeforeClear += FrozenCheck;
		}
		~ServiceContainer() { OnStop(); }

		void FrozenCheck(ICollection<WorkThread> self, IDisposable arg) { FrozenCheck(self); }
		void FrozenCheck(ICollection<WorkThread> self)
		{
			if (Interlocked.Read(ref _running) == 1 && Interlocked.Read(ref _collectionLock) == 1)
				throw new InvalidOperationException("Can not modify _jobs while running, collection is frozen!");
		}

		static readonly string ROUTE_BY_NS_TYPE_NAME = typeof(Subscribe.RouteByNameSpace).Name;
		static NsInfo[] GetCollectionNames(Settings settings)
		{
			var nsList = new List<NsInfo>();
			settings.Routes.ForEach(r => AttachNs(nsList, r));
			if (nsList.IsNullOrEmpty())
				throw new InvalidOperationException("Unable to find any configured RouteByNameSpace item, at least 1 is required");

			return (from n in nsList 
					group n by n.ToString() into ng 
					select ng.First()).ToArray();
		}

		static void AttachNs(ICollection<NsInfo> nsCollection, Subscribe.Decorator r)
		{
			if (r == null)
				return;

			if (r is Subscribe.RouteByNameSpace)
			{
				var nsr = r as OverNear.App.StarGate.Subscribe.RouteByNameSpace;
				if (nsr != null)
					nsCollection.Add(new NsInfo(nsr.NameSpace));
			}
			if (r.Trigger != null && r.Trigger is Subscribe.Decorator)
				AttachNs(nsCollection, r.Trigger as Subscribe.Decorator);
		}

		static readonly Regex NONE_WORDS = new Regex(@"[^a-z0-9_]+", RegexOptions.Compiled);

		long _collectionLock = 0;
		long _running = 0;
		protected override void OnStart(string[] args)
		{
			if (Interlocked.CompareExchange(ref _running, 1, 0) == 0)
			{
				try
				{
					if(_verboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Debug("OnStart: begins");

					Settings settings = Settings.ReadFromAppConfig();
					string cleanName = NONE_WORDS.Replace(settings.ReplicaName, string.Empty).ToLower().Trim();

					FixInfo fixInfo = null;
					var allResets = new List<ManualResetEvent>();
					if (HasBootStrapCommand(args))
					{
						InitBootStrap(args, cleanName, settings, allResets);
						if (HasContinueFlag(args))
							InitWormHole(cleanName, settings, allResets);
						else //force quit when done
						{
							ThreadPool.QueueUserWorkItem(o =>
							{
								WaitHandle.WaitAll(allResets.ToArray());
								_logger.Info("Bootstrap completed, stopping app now!");
								this.Dispose();
							});
						}
					}
					else if ((fixInfo = HasFixRepoCommand(args)) != null) //fix broken repo
					{
						var rp = new Repo.ReadStateElasticRepo();
						ReadState rs = rp.Load(fixInfo.RepoName);
						if (rs != null)
						{
							_logger.InfoFormat("Existing repo config found: {0}. Updating to value: {1}", fixInfo.RepoName, fixInfo.When);
							rp.UpdateTimeStamp(fixInfo.RepoName, new BsonTimestamp(fixInfo.When.ToUnixTime(), 0));
						}
						else
						{
							_logger.InfoFormat("No existing repo config for: {0}. Creating a new one w/ value: {1}", fixInfo.RepoName, fixInfo.When);
							rp.Create(new ReadState
							{
								Id = fixInfo.RepoName,
								TimeStamp = new BsonTimestamp(fixInfo.When.ToUnixTime(), 0),
							});
						}
					}
					else //normal ops, none bootstrap
					{
						string n = GetCopyRepoStateName(args);
						if (!string.IsNullOrWhiteSpace(n)) //copy command exists!
							CopyReadState(n, cleanName, settings);
						else
						{
							//ReadState rs = settings.ReadStateRepo.Load(cleanName);
							//if (rs == null || rs.TimeStamp.Value <= 0)
							//	throw new InvalidOperationException("CAN NOT START WORMHOLE, PLEASE BOOTSTRAP FIRST or --copy=<existing repo instance name>!");

							ReadState rs = settings.ReadStateRepo.Load(cleanName);
							if (rs == null)
							{
								_logger.WarnFormat("No existing configuration, creating one for {0}", cleanName);
								settings.ReadStateRepo.Create(new ReadState
								{
									Id = cleanName,
									TimeStamp = new BsonTimestamp(1, 0),
								});
							}
							else
								_logger.InfoFormat("Using existing configuration for {0}", cleanName);
						}
						InitWormHole(cleanName, settings, allResets); //always run
					}
					Interlocked.Exchange(ref _collectionLock, 1);
					if (_verboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Debug("OnStart: exits");
				}
				catch (Exception ex)
				{
					Interlocked.Exchange(ref _running, 0);
					Interlocked.Exchange(ref _collectionLock, 0);
					var sb = new StringBuilder("OnStart: ");
					sb.AppendItems(args);
					_logger.Error(sb.ToString(), ex);
					throw;
				}
			}
			else
				_logger.Warn("OnStart: Already running");
		}

		public void StartConsole(params string[] args)
		{
			OnStart(args);
		}

		#region onstart helpers

		bool HasBootStrapCommand(string[] args)
		{
			var cleanKeywords = new HashSet<string> { "--bootstrap", "--boot", "--clear", "--clean", "--cls" };
			bool clearState = args.Any(a => !string.IsNullOrWhiteSpace(a) && cleanKeywords.Contains(a.ToLower()));
			return clearState;
		}

		class FixInfo 
		{
			public string RepoName;
			public DateTime When;
		}

		FixInfo HasFixRepoCommand(string[] args)
		{
			if (args.IsNullOrEmpty())
				return null;

			FixInfo rp = null;
			var fixkws = new HashSet<string> { "--fix", "--fixrepo", };
			if (!string.IsNullOrWhiteSpace(args.FirstOrDefault()) && fixkws.Contains(args.FirstOrDefault().ToLower()))
			{
				string repo = args[1];
				DateTime fixdate;
				if (!string.IsNullOrWhiteSpace(repo))
				{
					if (args.Length <= 2 || string.IsNullOrWhiteSpace(args[2]) || !DateTime.TryParse(args[2], out fixdate) || fixdate.IsDefault())
					{
						_logger.WarnFormat("fix repo date is assumed to be NOW (UTC)");
						fixdate = DateTime.UtcNow;
					}
					rp = new FixInfo { When = fixdate, RepoName = repo };
				}
				else
					throw new ArgumentException("--fix missing repo name");
			}
			return rp;
		}

		void InitBootStrap(string[] args, string cleanName, Settings settings, ICollection<ManualResetEvent> allResets)
		{
			string u = args.FirstOrDefault(a => a.StartsWith("mongodb://"));
			if (string.IsNullOrWhiteSpace(u))
				throw new ArgumentException("CLEAN flag is provided but an explicit mongodb URL is missing!");
			else
				_logger.WarnFormat("BOOTSTRAPING {0}", u);

			NsInfo[] dbInfos = GetCollectionNames(settings);
			//if (_verboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
			//	_logger.DebugDeepCollection("Doing work for collections: {0}", dbInfos);

			var mu = new MongoUrl(u);
			IWorkUnit boom = new BigBang(
						cleanName,
						mu.DatabaseName,
						settings.Routes,
						() => new Repo.BootstrapReader(mu, true, _hostedDb, dbInfos)
						{
							MaxPoolSize = settings.ReadThreads.Count * 4,
							VerboseLog = _verboseLog,
						},
						settings.ReadStateRepo,
						settings.BasePathSettings != null ? settings.BasePathSettings.Path : null)
			{
				ClearState = true,
				VerboseLog = _verboseLog,
			};

			var bootstrapResetHandle = new ManualResetEvent(false);
			allResets.Add(bootstrapResetHandle);
			IWorkUnit noErr = new NoExceptionWork(boom, bootstrapResetHandle);
			_jobs.Add(new WorkThread(noErr));
		}

		static readonly Regex COPY_RE = new Regex(@"^--?(?:copy|cp)=(\w+)", RegexOptions.IgnoreCase);

		string GetCopyRepoStateName(string[] args)
		{
			string n = (from a in args
						let m = COPY_RE.Match(a)
						where m != null && m.Success && m.Groups.Count > 1
						select m.Groups[1].Value).FirstOrDefault();
			return n;
		}

		void CopyReadState(string n, string cleanName, Settings settings)
		{
			string cn = NONE_WORDS.Replace(n, string.Empty);
			ReadState rs = settings.ReadStateRepo.Load(cn);
			if (rs == null || rs.TimeStamp.Value <= 0)
				throw new InvalidOperationException("CAN NOT --copy= " + cn + " entry does not exists.");
			else
			{
				rs.Id = cn; //swap name & save!
				_logger.InfoFormat("Copying settings from {0} to {1}: {2}", cn, cleanName, rs.ToRs().ToJSON());
				settings.ReadStateRepo.Create(rs);
			}
		}

		bool HasContinueFlag(string[] args)
		{
			var CONTINUE_RE = new Regex(@"^--?(?:continue|cont)$", RegexOptions.IgnoreCase);
			bool ok = args.Any(CONTINUE_RE.IsMatch);
			if (ok)
				_logger.Info("Bootstrap Completed. Will start wormhole next!");

			return ok;
		}

		class SgParam
		{
			public ReadThread ReadThread;
			public string MatchStr;
			public MongoUrl Uri;
			public bool IsSingleInstance;
		}

		void InitWormHole(string cleanName, Settings settings, ICollection<ManualResetEvent> allResets)
		{
			var stargateParams = new List<SgParam>();
			foreach (ReadThread rt in settings.ReadThreads)
			{
				try
				{
					var p = new SgParam
					{
						ReadThread = rt,
						MatchStr = rt.Match,
						IsSingleInstance = rt.MasterOnly,
					};

					if (_hostedDb)
						p.Uri = new MongoUrl(rt.Path);
					else
					{
						var re = new Regex(rt.Match, RegexOptions.IgnoreCase);
						var fp = new Repo.PathFinder(rt.Path) { VerboseLog = _verboseLog };
						p.Uri = fp.Match(re);
					}
					stargateParams.Add(p);
				}
				catch (MongoConnectionException mce)
				{
					_logger.Warn(mce.Message);
				}
				catch (Exception ex)
				{
					_logger.Error(ex);
					throw;
				}
			}
			if (stargateParams.IsNullOrEmpty())
				throw new ConfigurationErrorsException("ReadThreads setting for StarGate configuration section is empty!");

			foreach (var p in stargateParams)
			{
				IWorkUnit warp = new WormHole(
					cleanName,
					p.Uri.DatabaseName,
					settings.Routes,
					() => new Repo.OpLogReader(p.Uri, p.IsSingleInstance, _hostedDb)
					{
						MaxPoolSize = settings.ReadThreads.Count * 5,
					},
					settings.ReadStateRepo,
					settings.BasePathSettings != null ? settings.BasePathSettings.Path : null)
				{
					VerboseLog = _verboseLog,
				};

				IWorkUnit triggerWaitStart = new WaitForTriggerWork(warp, allResets.ToArray());
				IWorkUnit loop = new LoopDelayWork(new NoExceptionWork(triggerWaitStart), settings.NoMasterSleep);
				_jobs.Add(new WorkThread(loop));
			}
		}

		#endregion

		protected override void OnStop()
		{
			if (Interlocked.CompareExchange(ref _running, 2, 1) == 1)
			{
				try
				{
					if (_verboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Debug("OnStop: Killing logic thread...");
					if (!_jobs.IsNullOrEmpty())
					{
						_jobs.ForEach(t => t.Dispose());
						_jobs.Clear();
					}
					if (_verboseLog.HasFlag(VerboseLogLevel.ServiceLogic))
						_logger.Info("OnStop: Teardown completed");
				}
				catch (Exception ex)
				{
					_logger.Error("OnStop", ex); //silence errors...
				}
				finally
				{
					Interlocked.Exchange(ref _running, 0);
				}
			}
			else
				_logger.Warn("OnStop: Already stopped");
		}

		public void StopConsole()
		{
			OnStop();
		}
	}
}
