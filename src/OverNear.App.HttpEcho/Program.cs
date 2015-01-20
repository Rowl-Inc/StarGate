using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using MediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;
using System.Net.Http.Formatting;

using OverNear.Infrastructure;
using log4net;

namespace OverNear.App.HttpEcho
{
	public sealed class Program : IDisposable
	{
		static readonly ILog _logger = LogManager.GetLogger(typeof(Program));

		const int DEFAULT_PORT = 8080;
		const string SELF_HOST = "http://localhost:8080";

		readonly string _selfHostPath;
		public Uri BasePath { get { return new Uri(_selfHostPath); } }

		readonly HttpSelfHostConfiguration _config;

		public Program(int port = 0)
		{
			if (port <= 0)
				port = DEFAULT_PORT;
			if(port <= 80)
				throw new ArgumentOutOfRangeException("port can not be <= 80");

			_selfHostPath = "http://localhost:" + port;
			_config = new HttpSelfHostConfiguration(_selfHostPath);
			_logger.InfoFormat("HttpEcho configured at: {0}", _selfHostPath);

			var defaults = new { controller = "Reflection", action = "Echo", id = RouteParameter.Optional };
			_config.Routes.MapHttpRoute("action extension w/ id", "{controller}/{action}.{ext}/{id}", defaults);
			_config.Routes.MapHttpRoute("action w/ id extension", "{controller}/{action}/{id}.{ext}", defaults);
			_config.Routes.MapHttpRoute("default w/ id", "{controller}/{action}/{id}", defaults);
			
			_logger.DebugFormat("HttpEcho default controller/action: {0}/{1}", defaults.controller, defaults.action);

			_config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/html"));
			_config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/x-javascript"));
			_config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/javascript"));
			_config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/x-javascript"));
			_config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/x-json"));
			_config.Formatters.JsonFormatter.Indent = true;

			_config.Formatters.JsonFormatter.MediaTypeMappings.Add(new UriPathExtensionMapping("json", "application/json"));
			_config.Formatters.JsonFormatter.MediaTypeMappings.Add(new QueryStringMapping("json", "true", "application/json"));
			_config.Formatters.XmlFormatter.MediaTypeMappings.Add(new UriPathExtensionMapping("xml", "application/xml"));
			_config.Formatters.XmlFormatter.MediaTypeMappings.Add(new QueryStringMapping("xml", "true", "application/xml"));
			_config.Formatters.XmlFormatter.Indent = true;

			//_config.Filters.Add(
		}

		~Program() { Dispose(); }
		int _disposed = 0;
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
			{
				_logger.Info("Disposing");
			}
		}

		bool PeekConsole()
		{
			try
			{
				return Console.In.Peek() >= 0;
			}
			catch
			{
				return false;
			}
		}

		int _blockedOnce = 0;
		bool KeepBlocking()
		{
			bool isRunning = Interlocked.CompareExchange(ref _disposed, 0, 0) == 0;
			if (Interlocked.CompareExchange(ref _blockedOnce, 1, 0)==0)
				Console.WriteLine("Press ESC to quit.");

			if (isRunning && Environment.UserInteractive && PeekConsole())
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

		public void Run()
		{
			_logger.Info("Starting HttpEcho.");
			using (HttpSelfHostServer server = new HttpSelfHostServer(_config))
			{
				try
				{
					server.OpenAsync().Wait();
					_logger.Debug("HttpEcho listening...");

					while (KeepBlocking())
					{
						Thread.Sleep(10);
					}
				}
				finally
				{
					server.CloseAsync().Wait();
				}
			}
			_logger.Info("HttpEcho ended gracefully.");
		}

		public static void Main(string[] args)
		{
			DateTime started = DateTime.UtcNow;
			try
			{
				_logger.DebugFormat("Enter Main @ {0}", started);

				int port = DEFAULT_PORT;
				if (!args.IsNullOrEmpty())
					int.TryParse(args.FirstOrDefault(), out port);

				using (var logic = new Program(port))
				{
					logic.Run();
				}
			}
			catch (Exception ex)
			{
				_logger.Fatal("HttpEcho @ " + SELF_HOST, ex);
			}
			finally
			{
				DateTime stopped = DateTime.UtcNow;
				_logger.DebugFormat("Exitting HttpEcho at {0}. Uptime: {1}", stopped, stopped - started);
			}
		}


	}
}
