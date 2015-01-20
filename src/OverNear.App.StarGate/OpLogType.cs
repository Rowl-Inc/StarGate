using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace OverNear.App.StarGate
{
	/// <summary>
	/// <see cref="https://github.com/krickert/JavaMongoDBOpLogReader/blob/master/src/main/java/com/krickert/mongodb/oplog/MongoOplogOperation.java"/>
	/// </summary>
	[Flags]
	[Serializable]
	[XmlType]
	public enum OpLogType
	{
		/// <summary>
		/// !
		/// </summary>
		[XmlEnum]
		Unknown = 0,
		/// <summary>
		/// n
		/// </summary>
		[XmlEnum]
		NoOp = 1,
		/// <summary>
		/// i
		/// </summary>
		[XmlEnum]
		Insert = 2,
		/// <summary>
		/// u
		/// </summary>
		[XmlEnum]
		Update = 4,
		/// <summary>
		/// d
		/// </summary>
		[XmlEnum]
		Delete = 8,
		/// <summary>
		/// c
		/// </summary>
		[XmlEnum]
		Command = 16,
		/// <summary>
		/// Any or all types of command
		/// </summary>
		[XmlEnum]
		Any = Unknown | NoOp | Insert | Update | Delete | Command,
		/// <summary>
		/// All writes (and remove) commands
		/// </summary>
		[XmlEnum]
		Writes = Insert | Update | Delete,
		/// <summary>
		/// None writes (and remove) commands
		/// </summary>
		[XmlEnum]
		NoneWrites = NoOp | Command,
		/// <summary>
		/// Insert or update only
		/// </summary>
		[XmlEnum]
		Upsert = Insert | Update,
	}
}
