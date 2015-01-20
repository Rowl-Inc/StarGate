using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using System.Web;
using System.Net;
using System.IO;
using System.Xml.Serialization;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using log4net;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Take a list of commands and push it to all the configured publishers
	/// </summary>
	[Serializable]
	public class PublishChain : Trigger, IPublisher
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public event Action<IPublisher, IContext> OnSuccess;
		public event Action<IPublisher, IContext, Exception> OnError;

		public PublishChain() { } //for serializer compatibility
		public PublishChain(ICollection<Trigger> publishers) : this()
		{
			if (publishers.IsNullOrEmpty())
				throw new ArgumentException("publishers can not be null or empty");

			publishers.ForEach(p => _publishers.Add(p));
		}

		[XmlIgnore]
		TriggerList _publishers = new TriggerList();
		/// <summary>
		/// Chained publishers to be triggered
		/// </summary>
		[XmlArray]
		public TriggerList Publishers
		{
			get { return _publishers; }
			set
			{
				if (value == null)
					_publishers.Clear();
				else
					_publishers = value;
			}
		}

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			if (_publishers.IsNullOrEmpty())
				return;

			foreach (Trigger t in Publishers)
			{
				try
				{
					if (t != null)
					{
						t.Execute(context);
						if (OnSuccess != null)
							OnSuccess(this, context);
					}
					else if (OnError != null)
						OnError(this, context, new InvalidOperationException("A publisher within the chain is not of type Trigger!"));
				}
				catch (Exception lex)
				{
					_logger.Error("Execute: [inner loop]", lex);
				}
			}
		}

		public override void Reset()
		{
			if (_publishers.Count == 0)
				return;

			foreach (Trigger p in _publishers)
			{
				p.Reset();
			}
		}

		public override string ToString()
		{
			var sb = new StringBuilder("PublishChain: ");
			sb.AppendItems(_publishers);
			return sb.ToString();
		}
	}
}
