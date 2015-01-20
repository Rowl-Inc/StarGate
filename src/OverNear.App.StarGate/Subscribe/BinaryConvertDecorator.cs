using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Threading;
using log4net;
using System.Xml;
using System.Xml.Serialization;
using System.Web;
using System.Net;
using System.IO;

using MongoDB.Bson;
using OverNear.Infrastructure;


namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// Converts Payload Bson fields to big-endian byte[] whenever possible
	/// </summary>
	[Serializable]
	public class BinaryConvertDecorator : Decorator
	{
		protected static ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		public BinaryConvertDecorator() : base() { } //for serializer
		/// <summary>
		/// CTOR
		/// </summary>
		/// <param name="fields">CSV field names case sensitive of the fields to convert to binary</param>
		/// <param name="ingest">Child trigger for chained call</param>
		public BinaryConvertDecorator(string fields, Trigger ingest)
		{
			Fields = fields;
			Trigger = ingest;
		}

		[XmlIgnore]
		string _field = string.Empty;
		[XmlIgnore]
		readonly object _flock = new object(); //field lock
		[XmlIgnore]
		readonly protected HashSet<string> _fnames = new HashSet<string>(); //unique field names
		/// <summary>
		/// CSV field names case sensitive of the fields to convert to binary
		/// </summary>
		[XmlAttribute]
		public virtual string Fields
		{
			get { lock(_flock) return _field; }
			set
			{
				if (value != null && _field.GetHashCode() == value.GetHashCode())
					return;

				lock (_flock)
				{
					_fnames.Clear();
					if (!string.IsNullOrWhiteSpace(_field = value.TrimToEmpty()))
					{
						(from f in _field.Split(',', ' ', '\t', '\r', '\n')
						 where !string.IsNullOrWhiteSpace(f)
						 select f).ForEach(f => _fnames.Add(f));
					}
				}
			}
		}

		/// <summary>
		/// Use this thread safe handle
		/// </summary>
		[XmlIgnore]
		protected ICollection<string> FieldNames 
		{ 
			get { lock(_flock) return _fnames; } 
		}

		//holds the mapping instruction baseon bsontype
		static readonly Dictionary<BsonType, Func<BsonValue, byte[]>> CONVERT_MAP = new Dictionary<BsonType, Func<BsonValue, byte[]>>
		{
			//{ BsonType.Binary, o => o.AsByteArray }, //no need to convert, already in byte[]!
			//{ BsonType.DateTime, o => DataConverter.BigEndian.GetBytes(o.AsDateTime.ToUniversalTime().ToUnixTime()) },
			{ BsonType.Double, o => DataConverter.BigEndian.GetBytes(o.AsDouble) },
			{ BsonType.Int32, o => DataConverter.BigEndian.GetBytes(o.AsInt32) },
			{ BsonType.Int64, o => DataConverter.BigEndian.GetBytes(o.AsInt64) },
			{ BsonType.ObjectId, o => o.AsByteArray }, //natively big endian
			{ BsonType.String, o => Encoding.Default.GetBytes(o.AsString) }, //might need to flip endian-ness here... not sure!
		};

		public override void Execute(IContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Trigger == null)
				throw new InvalidOperationException("Trigger is null or not set!");

			if (context.Payload == null || !context.Payload.IsBsonDocument)
				Trigger.Execute(context); //skip processing...
			else
			{
				try
				{
					BsonDocument d = context.Payload.AsBsonDocument;
					var toConvert = (from e in d.Elements
									 where FieldNames.Contains(e.Name) &&
										  e.Value != null &&
										  !e.Value.IsBsonNull &&
										  CONVERT_MAP.ContainsKey(e.Value.BsonType)
									 let convertFunc = CONVERT_MAP[e.Value.BsonType]
									 let n = new { el = e, arr = convertFunc(e.Value) }
									 select n).ToArray();
					if (!toConvert.IsNullOrEmpty())
					{
						foreach (var c in toConvert) //doing this for easy debugging, lambda would be nicer
						{
							c.el.Value = c.arr;
						}
						context.Payload = d; //should be the same ref, probaly not necessary
					}
					Trigger.Execute(context); //always process if successful
				}
				catch (Exception ex)
				{
					_logger.Error("Execute(...) Fields: " + Fields, ex);
					throw; //needs to throw or the error in config will not be discovered
				}
			}
		}

		//class ChildMapping 
		//{
		//	public string Parent;
		//	public readonly HashSet<string> FieldNames = new HashSet<string>();
		//}

		//static void ConvertFields(BsonDocument d, ChildMapping m)
		//{
		//	if (!m.FieldNames.IsNullOrEmpty())
		//	{
		//		(from e in d.Elements
		//		 where m.FieldNames.Contains(e.Name) &&
		//			  e.Value != null &&
		//			  !e.Value.IsBsonNull &&
		//			  CONVERT_MAP.ContainsKey(e.Value.BsonType)
		//		 let convertFunc = CONVERT_MAP[e.Value.BsonType]
		//		 let n = new { el = e, arr = convertFunc(e.Value) }
		//		 select n).ForEach(c => c.el.Value = c.arr);
		//	}
		//}

	}
}
