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
	[XmlInclude(typeof(CallOnceDecorator))]
	[XmlInclude(typeof(ElasticIndexDecorator))]
	[XmlInclude(typeof(BinaryConvertDecorator))]
	[XmlInclude(typeof(TransformJsDecorator))]
	[XmlInclude(typeof(FullObjectDecorator))]
	[XmlInclude(typeof(ElasticSearchPublisher))]
	[XmlInclude(typeof(DynamicRestPublisher))]
	[XmlInclude(typeof(RestPublisher))]
	[XmlInclude(typeof(NullPublisher))]
	[XmlInclude(typeof(Route))]
	[XmlInclude(typeof(JsInclude))]
	[XmlInclude(typeof(Decorator))]
	[XmlInclude(typeof(TriggerList))]
	[XmlInclude(typeof(PublishChain))]
	public abstract class Trigger
	{
		public abstract void Execute(IContext context);

		/// <summary>
		/// Reset singleton state if applicable
		/// </summary>
		public virtual void Reset() { }
	}
}
