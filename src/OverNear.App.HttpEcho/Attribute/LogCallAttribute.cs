using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Http.Filters;
using System.Web.Http.Controllers;
using log4net;
using System.Threading;
using OverNear.Infrastructure;

namespace OverNear.App.HttpEcho.Attribute
{
	public class LogCallAttribute : ActionFilterAttribute
	{
		static readonly ILog _logger = LogManager.GetLogger(typeof(LogCallAttribute).FullName.Replace("LogCallAttribute", "LogCall"));

		readonly bool _dumpHeader, _testOnly;
		public LogCallAttribute(bool dumpHeader = false, bool testModeOnly = false)
		{
			_dumpHeader = dumpHeader;
			_testOnly = testModeOnly;
		}

		long _created = 0;
		DateTime Created
		{
			get { return DateTime.SpecifyKind(new DateTime(Interlocked.Read(ref _created)), DateTimeKind.Utc); }
			set { Interlocked.Exchange(ref _created, value.ToUniversalTime().Ticks); }
		}

		bool IsDisabled
		{
			get { return !_logger.IsDebugEnabled || _testOnly; }
		}

		public override void OnActionExecuting(HttpActionContext actionContext)
		{
			if (IsDisabled)
			{
				base.OnActionExecuting(actionContext); //early exit
				return;
			}
			Created = DateTime.UtcNow;
			base.OnActionExecuting(actionContext);
		}

		static readonly TimeSpan ONE_MINUTE = TimeSpan.FromMinutes(1);
		static readonly TimeSpan ONE_SECOND = TimeSpan.FromSeconds(1);

		public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
		{
			if (IsDisabled)
				return;

			TimeSpan took = DateTime.UtcNow.Subtract(Created);
			string ts;
			if (took < ONE_SECOND)
				ts = took.TotalMilliseconds.ToString("000.0") + "ms";
			else if (took < ONE_MINUTE)
				ts = took.TotalSeconds.ToString("00.000") + "s";
			else
				ts = took.ToString();

			var request = actionExecutedContext.Request;
			_logger.InfoFormat("{0} {1} {2} {3}", ts, request.Headers.Host, request.Method, request.RequestUri.PathAndQuery);

			var echoRsp = new Models.EchoResponse(actionExecutedContext.ActionContext.ControllerContext);
			if (_dumpHeader)
				_logger.Debug(echoRsp.ToJSON());
			else
				_logger.DebugFormat("{0}\r\n{1}", echoRsp.Content.Headers.ToJSON(), echoRsp.Content.Text);
		}
	}
}
