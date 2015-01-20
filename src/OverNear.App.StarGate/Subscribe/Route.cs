using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace OverNear.App.StarGate.Subscribe
{
	[XmlInclude(typeof(RouteByNameSpace))]
	[XmlInclude(typeof(CDataText))]
	[XmlInclude(typeof(RouteByJsPredicate))]
	public abstract class Route : Decorator
	{
		/// <summary>
		/// When true, will allow continuous evaluation down the chain of responsibility eventhough code is evaluated.
		/// </summary>
		[XmlAttribute]
		public virtual bool Continue { get; set; }

		public abstract TaskProcessState Evaluate(IContext context);
	}
}
