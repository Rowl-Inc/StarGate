using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Threading;
using log4net;
using System.Xml;
using System.Xml.Serialization;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Subscribe
{
	/// <summary>
	/// This decorator takes a partial update object and replace the o2 field with full object instead of the default _id only
	/// </summary>
	[Serializable]
	public class FullObjectDecorator : Decorator
	{
		static readonly ILog _logger = LogManager.GetLogger("OverNear.App.StarGate");

		readonly bool _hostedDb = false;

		public FullObjectDecorator()
		{
			_hostedDb = ConfigurationManager.AppSettings.ExtractConfiguration("_hostedDb", _hostedDb);
		}
		public FullObjectDecorator(Trigger ingest) : this()
		{
			Trigger = ingest;
		}

		public override void Execute(IContext context) //don't catch/rethrow here, do it at the upper layer
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Trigger == null)
				throw new InvalidOperationException("Trigger is null or not set!");

			try
			{
				if (context.Original.Operation == OpLogType.Update &&
					context.Original.Source != null && context.Original.Source.Contains("_id"))
				{
					BsonValue sid;
					if ((sid = context.Original.Source["_id"]) == null)
						_logger.WarnFormat("Execute: _id is null for Original.Source of {0}", context);
					else if (context.CurrentRepo == null)
						_logger.WarnFormat("Execute: current context repo is null for {0}", context);
					else if (string.IsNullOrWhiteSpace(context.Original.NameSpace))
						_logger.WarnFormat("Execute: Original.NameSpace is null or empty! {0}", context);
					else
					{
						int ix = context.Original.NameSpace.IndexOf('.');
						if (ix <= 0)
							_logger.WarnFormat("Execute: Unable to split db & col name for Original.NameSpace: {0}", context);
						else
						{
							string dbname, colname;
							if (_hostedDb)
								dbname = context.ConfiguredDb;
							else
								dbname = context.Original.NameSpace.Remove(ix);

							if (string.IsNullOrWhiteSpace(dbname))
								_logger.WarnFormat("Execute: dbName is blank after NS split: {0}", context);
							else if (string.IsNullOrWhiteSpace(colname = context.Original.NameSpace.Substring(ix + 1)))
								_logger.WarnFormat("Execute: colName is blank after NS split: {0}", context);
							else
							{
								MongoDatabase db;
								if (context.CurrentRepo.Database.Name != dbname)
									db = context.CurrentRepo.Server.GetDatabase(dbname);
								else
									db = context.CurrentRepo.Database;
								if (db == null)
									_logger.WarnFormat("Execute: db {0} can not be obtained for {1}", dbname, context);
								else
								{
									MongoCollection col = db.GetCollection(colname);
									if (col == null)
										_logger.WarnFormat("Execute: col {0} can not be obtained for {1}", dbname, context);
									else
									{
										BsonDocument fullObj = col.FindOneByIdAs<BsonDocument>(sid);
										if (fullObj == null)
										{
											if(context.VerboseLog.HasFlag(VerboseLogLevel.Request))
												_logger.DebugFormat("Execute: unable to fetch full obj for {0}", context);
										}
										else
										{
											context.Original.Source = fullObj;
											if (context.Payload != null && context.Payload.IsBsonDocument)
											{
												BsonDocument d = context.Payload.AsBsonDocument;
												if (d != null && d.Contains("o2"))
													d["o2"] = fullObj;
											}
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.Error(string.Format("Execute: {0}", context), ex);
			}
			Trigger.Execute(context); //always do this regardless...
		}

	}
}
