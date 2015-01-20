using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson;

using OverNear.Infrastructure;

namespace OverNear.App.StarGate.Repo
{
	public interface IReadStateRepo
	{
		ReadState Load(string id);
		ICollection<ReadState> Load(IEnumerable<string> ids);
		
		/// <summary>
		/// Try and create a new readsate, if an id exists, do not override
		/// </summary>
		/// <param name="state">initial state to save</param>
		/// <returns>true if success, false if an existing state with duplicating id exists</returns>
		bool Create(ReadState state);

		/// <summary>
		/// Atomic update of the timestamp only if provided value is higher than what's stored in the database.
		/// Function will return boolean success.
		/// </summary>
		/// <param name="id">id of read state thread, will be trimmed and lowered</param>
		/// <param name="newTimeStamp">
		/// Actual time stamp value to set if current value in db is lower than this or lower than the optionally provided lastTimeStamp 
		/// </param>
		/// <param name="lastTimeStamp">
		/// Optional. Only update when lastTimeStamp matches what currently in the database for concurrency issues.
		/// This ensures that only 1 thread is running using this specific id at any time.</param>
		/// <returns>true if success, false if not able to update</returns>
		bool UpdateTimeStamp(string id, BsonTimestamp newTimeStamp, BsonTimestamp lastTimeStamp = null);

		void Clear(string id);

		void ClearAll();
	}
}
