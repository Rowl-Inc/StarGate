using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	/// <summary>
	/// Name Space Info
	/// </summary>
	public class NsInfo
	{
		readonly string _raw;
		public string Raw
		{
			get { return _raw; }
		}

		public NsInfo(string ns)
		{
			if (string.IsNullOrWhiteSpace(ns))
				throw new ArgumentException("ns can not be null or blank");

			_raw = ns;
			int ix = ns.IndexOf('.');
			if (ix > 0)
			{
				Database = ns.Remove(ix);
				Collection = ns.Substring(ix + 1);
			}
		}

		string _db;
		/// <summary>
		/// Mongpo Database Name
		/// </summary>
		public string Database
		{
			get { return _db; }
			set 
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Database can not be null or empty");

				_db = value;
			}
		}

		string _col;
		/// <summary>
		/// Mongo Collection Name
		/// </summary>
		public string Collection
		{
			get { return _col; }
			set
			{
				if (string.IsNullOrWhiteSpace(value))
					throw new ArgumentException("Collection can not be null or empty");

				_col = value;
			}
		}

		public override string ToString()
		{
			return string.Format("{0}.{1}", Database, Collection);
		}
	}
}
