using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;
using System.Net.Http;
using OverNear.Infrastructure;

namespace OverNear.App.HttpEcho.Models
{
	public class ContentInfo
	{
		const int MAX_PARSE_WAIT_MS = 1000 * 60; //in ms

		public ContentInfo() 
		{
			Headers = new Dictionary<string, IEnumerable<string>>();
		}
		public ContentInfo(HttpContent content) : this()
		{
			if (content == null)
				throw new ArgumentNullException("content");

			if (content.Headers != null)
			{
				foreach (var item in content.Headers)
					Headers.Add(item.Key, item.Value);
			}

			using (var ms = new MemoryStream())
			{
				content.CopyToAsync(ms).Wait(MAX_PARSE_WAIT_MS);
				ms.Flush();
				Length = ms.Length;

				if (ms.Length > int.MaxValue)
					throw new InvalidOperationException("Unable to read raw content, size " + ms.Length + " is bigger than int.Max @ " + int.MaxValue);

				ms.Position = 0;
				using (var br = new BinaryReader(ms))
				{
					Raw = br.ReadBytes((int)ms.Length);

					ms.Position = 0;
					using (var sr = new StreamReader(ms))
						Text = sr.ReadToEnd();
				}
			}
		}

		public IDictionary<string, IEnumerable<string>> Headers { get; set; }
		public long Length { get; set; }
		public byte[] Raw { get; set; }
		public string Text { get; set; }
	}
}
