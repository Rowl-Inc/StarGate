using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate.Repo
{
	public class NotMasterException : ApplicationException
	{
		public NotMasterException(string msg) : base(msg) { }
		public NotMasterException(string msg, Exception innerException) : base(msg, innerException) { }
	}
}
