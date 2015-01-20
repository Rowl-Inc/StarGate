using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate.Repo
{
	/// <summary>
	/// Throw this exception to stop the reader from reading
	/// </summary>
	public class FatalReaderException : ApplicationException
	{
		public FatalReaderException(string message)
			: base(message)
		{
		}

		public FatalReaderException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}
}
