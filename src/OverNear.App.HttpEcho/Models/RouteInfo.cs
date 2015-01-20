using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Web.Http;
using System.Web.Http.Routing;
using OverNear.Infrastructure;

namespace OverNear.App.HttpEcho.Models
{
	public class RouteInfo
	{
		public RouteInfo() { }
		public RouteInfo(IHttpRouteData data) : this()
		{
			if (data == null)
				throw new ArgumentNullException("data");

			if (data.Route != null)
				Route = data.Route.RouteTemplate;

			Value = data.Values;
		}

		public string Route { get; set; }
		public IDictionary<string, object> Value { get; set; }
	}
}
