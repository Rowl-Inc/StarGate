using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace OverNear.Infrastructure
{
	sealed class CookieSingleton
	{
		class Inner { static internal readonly CookieSingleton SINGLETON = new CookieSingleton(); }
		public static CookieSingleton Instance { get { return Inner.SINGLETON; } }

		readonly CookieContainer _container;

		private CookieSingleton() 
		{
			_container = new CookieContainer();
		}

		public CookieContainer Container { get { return _container; } }
	}
}
