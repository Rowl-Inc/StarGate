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
	[XmlInclude(typeof(TransformJsDecorator))]
	[XmlInclude(typeof(ElasticIndexDecorator))]
	[XmlInclude(typeof(BinaryConvertDecorator))]
	[XmlInclude(typeof(CallOnceDecorator))]
	[XmlInclude(typeof(FullObjectDecorator))]
	[XmlInclude(typeof(Route))]
	[XmlInclude(typeof(JsInclude))]
	public abstract class Decorator : Trigger
	{
		[XmlIgnore]
		protected Trigger _successTrigger;

		public virtual Trigger Trigger
		{
			get { return _successTrigger; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("Trigger");

				_successTrigger = value;
			}
		}

		public override void Reset() 
		{
			if (Trigger != null)
				Trigger.Reset();
		}
	}
}
