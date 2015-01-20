using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

using OverNear.Infrastructure;

namespace OverNear.App.StarGate
{
	public abstract class AbstractConfigCollection<T> : List<T>
		where T : class
	{
		protected readonly XmlSerializer SERIALIZER;

		public AbstractConfigCollection() //deserializer compatibility
		{
			SERIALIZER = new XmlSerializer(this.GetType());
		} 
		public AbstractConfigCollection(string serializedXml) : this()
		{
			XmlString = serializedXml;
		}
		public AbstractConfigCollection(FileInfo fileLocation) : this()
		{
			if (fileLocation == null)
				throw new ArgumentNullException();

			object o = null;
			using (StreamReader sr = File.OpenText(fileLocation.FullName))
			using (XmlReader reader = XmlReader.Create(sr, Constants.SETTINGS_R))
			{
				o = SERIALIZER.Deserialize(reader);
			}
			if (o != null && o is AbstractConfigCollection<T>)
			{
				AbstractConfigCollection<T> rl = o as AbstractConfigCollection<T>;
				rl.ForEach(this.Add);
			}
		}
		
		[XmlIgnore]
		public virtual string XmlString
		{
			get
			{
				var sb = new StringBuilder();
				using (XmlWriter writer = XmlWriter.Create(sb, Constants.SETTINGS_W))
				{
					SERIALIZER.Serialize(writer, this);
				}
				return sb.ToString();
			}
			protected set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentNullException();

				object o = null;
				using (var ms = new MemoryStream())
				using (var sr = new StreamWriter(ms))
				{
					sr.Write(value);
					sr.Flush();
					ms.Position = 0;

					using (XmlReader reader = XmlReader.Create(ms, Constants.SETTINGS_R))
						o = SERIALIZER.Deserialize(reader);
				}
				if (o != null && o is AbstractConfigCollection<T>)
				{
					this.Clear();
					AbstractConfigCollection<T> rl = o as AbstractConfigCollection<T>;
					rl.ForEach(this.Add);
				}
			}
		}

		public virtual void SaveToFile(FileInfo f)
		{
			if (File.Exists(f.FullName))
				File.Delete(f.FullName);

			using (StreamWriter sw = File.CreateText(f.FullName))
			using (XmlWriter writer = XmlWriter.Create(sw, Constants.SETTINGS_W))
				SERIALIZER.Serialize(writer, this);
		}

		public override string ToString()
		{
			return this.XmlString;
		}
	}
}
