using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Xml;
using System.Xml.Serialization;

using MongoDB.Bson;
using MongoDB.Bson.IO;
using OverNear.Infrastructure;


namespace OverNear.App.StarGate
{
	sealed internal class Constants
	{
		class Inner { static readonly internal Constants SINGLETON = new Constants(); }
		private Constants() { }
		public static Constants Instance { get { return Inner.SINGLETON; } }

		public readonly JsonWriterSettings STRICT_JSON = new JsonWriterSettings
		{
			OutputMode = JsonOutputMode.Strict,
			GuidRepresentation = GuidRepresentation.Standard,
#if DEBUG
			Indent = true,
			IndentChars = "\t",
			NewLineChars = "\r\n",
#endif
		};

		public static readonly XmlWriterSettings SETTINGS_W = new XmlWriterSettings
		{
#if DEBUG
			Indent = true,
			IndentChars = "\t",
			NewLineChars = "\r\n",
#endif
			NamespaceHandling = NamespaceHandling.OmitDuplicates,
			NewLineHandling = NewLineHandling.Replace,
			NewLineOnAttributes = false,
			ConformanceLevel = ConformanceLevel.Auto,
			OmitXmlDeclaration = true,
		};

		public static readonly XmlReaderSettings SETTINGS_R = new XmlReaderSettings
		{
			DtdProcessing = System.Xml.DtdProcessing.Ignore,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true,
			IgnoreWhitespace = true,
			ValidationType = System.Xml.ValidationType.None,
			ConformanceLevel = ConformanceLevel.Auto,
		};
	}
}
