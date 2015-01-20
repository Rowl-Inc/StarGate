using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Web.Http;

using log4net;
using OverNear.Infrastructure;
using OverNear.App.HttpEcho.Models;
using OverNear.App.HttpEcho.Attribute;

namespace OverNear.App.HttpEcho.Controllers
{
	public class ReflectionController : ApiController
	{
		[LogCall]
		[AcceptVerbs("GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "PATCH")]
		public EchoResponse Echo()
		{
			return new EchoResponse(this.ControllerContext);
		}

		[LogCall]
		[AcceptVerbs("GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "PATCH")]
		public HeaderInfo Headers()
		{
			var ec = new EchoResponse(this.ControllerContext);
			return ec.Headers;
		}

		[LogCall]
		[AcceptVerbs("GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "PATCH")]
		public HttpResponseMessage Content()
		{
			var ec = new EchoResponse(this.ControllerContext);
			var r = new HttpResponseMessage();
			if (ec.Content != null)
				r.Content = new StringContent(ec.Content.Text, Encoding.UTF8, "text/html");

			return r;
		}
	}
}
