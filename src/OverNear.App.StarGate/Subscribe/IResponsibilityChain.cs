using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate.Subscribe
{
	public interface IResponsibilityChain : ICollection<Route>
	{
		/// <summary>
		/// Execute logic
		/// </summary>
		/// <param name="context">execution context</param>
		void Evaluate(IContext context);

		/// <summary>
		/// Reset singleton states
		/// </summary>
		void Reset();

		/// <summary>
		/// Optional base path. If exists and actual route path is relative, will add this portion in
		/// </summary>
		string BasePath { get; }

		Uri GetAbsUrl(string path);
	}
}
