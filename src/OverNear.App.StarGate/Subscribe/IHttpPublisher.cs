using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;

namespace OverNear.App.StarGate.Subscribe
{
	public interface IHttpPublisher : IPublisher
	{
		event Action<IPublisher, IContext, HttpWebResponse> OnHttpSuccess;
		event Action<IPublisher, IContext, HttpWebResponse> OnHttpError;
	}
}
