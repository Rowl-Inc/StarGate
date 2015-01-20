using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;
using OverNear.Infrastructure;

namespace OverNear.App.HttpEcho.Models
{
	public class EchoResponse
	{
		public EchoResponse()
		{
			Created = DateTime.UtcNow;
		}
		public EchoResponse(HttpControllerContext context) : this()
		{
			if (context == null)
				throw new ArgumentNullException("context");

			Controller = context.Controller.ToString();
			Route = new RouteInfo(context.RouteData);

			if (context.Request == null)
				return;

			Headers = new HeaderInfo(context.Request.Headers);
			RequestUri = context.Request.RequestUri;

			if(context.Request.Method != null)
				Method = context.Request.Method.Method;

			if (context.Request.Version != null)
				Version = context.Request.Version.ToString();

			if (context.Request.Content != null)
				Content = new ContentInfo(context.Request.Content);
		}

		public DateTime Created { get; set; }
		public string Controller { get; set; }
		public RouteInfo Route { get; set; }
		public HeaderInfo Headers { get; set; }
		public string Method { get; set; }
		public Uri RequestUri { get; set; }
		public string Version { get; set; }
		public ContentInfo Content { get; set; }
	}
}
