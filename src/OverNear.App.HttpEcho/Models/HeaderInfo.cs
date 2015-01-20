using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Web.Http;
using System.Net.Http;
using System.Net.Http.Headers;
using OverNear.Infrastructure;

namespace OverNear.App.HttpEcho.Models
{
	public class HeaderInfo : Dictionary<string, IEnumerable<string>>
	{
		public HeaderInfo() { }
		public HeaderInfo(HttpRequestHeaders h)
			: this()
		{
			if (h == null)
				throw new ArgumentNullException("h");

			foreach (var item in h)
				this.Add(item.Key, item.Value);
		}
	}
}
