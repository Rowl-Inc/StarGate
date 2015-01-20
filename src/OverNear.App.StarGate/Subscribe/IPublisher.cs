using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;

namespace OverNear.App.StarGate.Subscribe
{
	public interface IPublisher
	{
		event Action<IPublisher, IContext> OnSuccess;
		event Action<IPublisher, IContext, Exception> OnError;
	}
}
