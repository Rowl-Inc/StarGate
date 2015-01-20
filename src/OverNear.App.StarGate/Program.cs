using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	sealed class Program : IDisposable
	{
		static readonly ILog _logger;

		static Program()
		{
			_logger = LogManager.GetLogger("OverNear.App.StarGate");
			_logger.Debug("Static CTOR OK");
		}

		public Program()
		{
			string startupLogo = @"

                            S T A R G A T E
                                _______                                
                        _,.--==###\_/=###=-.._                         
                    ..-'     _.--\\_//---.    `-..                     
                 ./'    ,--''     \_/     `---.   `\.                  
               ./ \ .,-'      _,,......__      `-. / \.                
             /`. ./\'    _,.--'':_:'""`:'`-..._    /\. .'\              
            /  .'`./   ,-':"":._.:"":._.:""+._.:`:.  \.'`.  `.            
          ,'  //    .-''""`:_:'""`:_:'""`:_:'""`:_:'`.     \   \           
         /   ,'    /'"":._.:"":._.:"":._.:"":._.:"":._.`.    `.  \          
        /   /    ,'`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_\     \  \         
       ,\\ ;     /_.:"":._.:"":._.:"":._.:"":._.:"":._.:"":\     ://,        
       / \\     /'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'\    // \.       
      |//_ \   ':._.:"":._.+"":._.:"":._.:"":._.:"":._.:"":._\  / _\\ \      
     /___../  /_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""'. \..__ |      
      |  |    '"":._.:"":._.:"":._.:"":._.:"":._.:"":._.:"":._.|    |  |      
      |  |    |-:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`|    |  |      
      |  |    |"":._.:"":._.:"":._.:"":._.:"":._.+"":._.:"":._.|    |  |      
      |  :    |_:'""`:_:'""`:_+'""`:_:'""`:_:'""`:_:'""`:_:'""`|    ; |       
      |   \   \.:._.:"":._.:"":._.:"":._.:"":._.:"":._.:"":._|    /  |       
       \   :   \:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'""`:_:'.'   ;  |        
        \  :    \._.:"":._.:"":._.:"":._.:"":._.:"":._.:"":,'    ;  /        
        `.  \    \..--:'""`:_:'""`:_:'""`:_:'""`:_:'""`-../    /  /         
         `__.`.'' _..+'._.:"":._.:"":._.:"":._.:"":.`+._  `-,:__`          
      .-''    _ -' .'| _________________________ |`.`-.     `-.._      
_____'   _..-|| :.' .+/;;';`;`;;:`)+(':;;';',`\;\|. `,'|`-.      `_____
  MJP .-'   .'.'  :- ,'/,',','/ /./|\.\ \`,`,-,`.`. : `||-.`-._        
          .' ||.-' ,','/,' / / / + : + \ \ \ `,\ \ `.`-||  `.  `-.     
       .-'   |'  _','<', ,' / / // | \\ \ \ `, ,`.`. `. `.   `-.       
                                   :              - `. `.              
";
			Console.WriteLine(startupLogo);
			_logger.Debug("CTOR OK");
		}

		~Program() { Dispose(); }
		int _disposed = 0;
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				_logger.Info("Disposing");
				if (Environment.UserInteractive)
				{
					try
					{
						IntPtr stdin = GetStdHandle(StdHandle.Stdin);
						CloseHandle(stdin);
					}
					catch //(Exception ex)
					{
						//_logger.Warn("Dispose issue with closing stdin handle", ex);
					}
				}
			}
			else
				_logger.Debug("Disposed already");
		}

		// P/Invoke:
		private enum StdHandle { Stdin = -10, Stdout = -11, Stderr = -12 };
		[DllImport("kernel32.dll")]
		private static extern IntPtr GetStdHandle(StdHandle std);
		[DllImport("kernel32.dll")]
		private static extern bool CloseHandle(IntPtr hdl);

		int _blockedOnce = 0;
		bool KeepBlocking()
		{
			bool isRunning = Interlocked.CompareExchange(ref _disposed, 0, 0) == 0;
			if (Interlocked.CompareExchange(ref _blockedOnce, 1, 0) == 0)
				Console.WriteLine("Press ESC to quit.");

			if (isRunning && Environment.UserInteractive && Console.In.Peek() >= 0)
			{
				Console.WriteLine("Press ESC to quit.");
				ConsoleKeyInfo k = Console.ReadKey();
				return k.Key != ConsoleKey.Escape;
			}
			else
			{
				if (!isRunning)
					Thread.Sleep(1000);

				return isRunning;
			}
		}

		public void Run(params string[] args)
		{
			_logger.Debug("Run Started");
			using (var svc = new ServiceContainer())
			{
				svc.Disposed += svc_Disposed;
				if (Environment.UserInteractive)
				{
					_logger.Debug("Run StartConsole in UserInteractive mode");
					svc.StartConsole(args);
					while (KeepBlocking())
					{
						Thread.Sleep(10);
					}
					_logger.Debug("Run StopConsole for UserInteractive mode");
					svc.StopConsole();
				}
				else
				{
					_logger.Debug("Run ServiceBase in background mode");
					ServiceBase.Run(svc);
				}
			}
			_logger.Debug("Run Ends");
		}

		void svc_Disposed(object sender, EventArgs e) //trigger top level app to dispose too!
		{
			this.Dispose();
		}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main(string[] args)
		{
			DateTime started = DateTime.UtcNow;
			try
			{
				_logger.InfoFormat("\r\n\r\n############## Main started at {0} ##############", started);
				using (var logic = new Program())
				{
					logic.Run(args);
				}
			}
			catch (Exception ex)
			{
				var sb = new StringBuilder("############## Main FATAL ERROR [input params]: ");
				sb.AppendItems(args);
				_logger.Fatal(sb.ToString(), ex);
			}
			finally
			{
				DateTime stopped = DateTime.UtcNow;
				_logger.InfoFormat("############## Main stopped at {0}. Uptime: {1} ##############\r\n\r\n", stopped, stopped - started);
#if DEBUG
				try
				{
					Console.WriteLine("Press Enter to QUIT");
					Console.ReadLine();
				}
				catch //(Exception ex)
				{
					//_logger.Warn("Main exit stopper broke", ex);
				}
#endif
			}
		}
	}
}
