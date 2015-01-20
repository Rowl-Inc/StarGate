using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;

namespace OverNear.Infrastructure
{
	/// <summary>
	/// Thread safe collection wrapper with trigger
	/// </summary>
	/// <typeparam name="T">Which ever type the collection is based on (where data is actually stored internally)</typeparam>
	[Serializable]
	public class CollectionTrigger<T> : ICollection<T>
	{
		/// <summary>
		/// OnBeforeAdd(original Storage collection, item added)
		/// </summary>
		public event Action<ICollection<T>, T> OnBeforeAdd;
		/// <summary>
		/// OnAfterAdd(original Storage collection, item added)
		/// </summary>
		public event Action<ICollection<T>, T> OnAfterAdd;
		/// <summary>
		/// OnBeforeRemove(original Storage collection, item removed)
		/// </summary>
		public event Action<ICollection<T>, T> OnBeforeRemove;
		/// <summary>
		/// OnAfterRemove(original Storage collection, item removed)
		/// </summary>
		public event Action<ICollection<T>, T> OnAfterRemove;
		/// <summary>
		/// OnBeforeClear(original Storage collection)
		/// </summary>
		public event Action<ICollection<T>> OnBeforeClear;
		/// <summary>
		/// OnAfterClear(original Storage collection)
		/// </summary>
		public event Action<ICollection<T>> OnAfterClear;

		/// <summary>
		/// thread safety
		/// </summary>
		readonly protected object _padlock = new object(); //TODO: change to reader/writer lock later on for better performance
		/// <summary>
		/// storage data
		/// </summary>
		readonly protected ICollection<T> _collection;

		protected readonly string FRIENDLY_NAME;

		/// <summary>
		/// Create a threadsafe collection with trigger capability
		/// </summary>
		/// <param name="storage">the orignal collection to wrap</param>
		public CollectionTrigger(ICollection<T> storage)
		{
			if (storage == null)
				throw new ArgumentNullException("storage");
			if (storage.IsReadOnly)
				throw new InvalidOperationException("storage.IsReadOnly can not be true!");

			_collection = storage;

			//build friendly name for ToString(...)
			string n = GetType().Name;
			int ix = n.IndexOf('`');
			if (ix > 0)
				n = n.Remove(ix);
			FRIENDLY_NAME = n + "<" + typeof(T).Name + ">";
		}

		protected enum TriggerTime
		{
			Before,
			After,
		}
		protected enum TriggerOp
		{
			Add,
			Remove,
			Clear,
		}

		protected virtual void TriggerClearEvent(TriggerTime when)
		{
			TriggerEvent(default(T), TriggerOp.Clear, when);
		}
		protected virtual void TriggerEvent(T item, TriggerOp op, TriggerTime when)
		{
			switch (op)
			{
				case TriggerOp.Add:
					switch (when)
					{
						case TriggerTime.Before:
							if (OnBeforeAdd != null)
								OnBeforeAdd(_collection, item);
							break;
						case TriggerTime.After:
							if (OnAfterAdd != null)
								OnAfterAdd(_collection, item);
							break;
						default:
							throw new NotImplementedException("TriggerEvent can not handle: TriggerTime." + when);
					}
					break;
				case TriggerOp.Remove:
					switch (when)
					{
						case TriggerTime.Before:
							if (OnBeforeRemove != null)
								OnBeforeRemove(_collection, item);
							break;
						case TriggerTime.After:
							if (OnAfterRemove != null)
								OnAfterRemove(_collection, item);
							break;
						default:
							throw new NotImplementedException("TriggerEvent can not handle: TriggerTime." + when);
					}
					break;
				case TriggerOp.Clear:
					switch (when)
					{
						case TriggerTime.Before:
							if (OnBeforeClear != null)
								OnBeforeClear(_collection);
							break;
						case TriggerTime.After:
							if (OnAfterClear != null)
								OnAfterClear(_collection);
							break;
						default:
							throw new NotImplementedException("TriggerEvent can not handle: TriggerTime." + when);
					}
					break;
				default:
					throw new NotImplementedException("TriggerEvent can not handle: TriggerOp." + op);
			}
		}

		public virtual void Add(T item)
		{
			lock (_padlock)
			{
				TriggerEvent(item, TriggerOp.Add, TriggerTime.Before);

				_collection.Add(item);

				TriggerEvent(item, TriggerOp.Add, TriggerTime.After);
			}
		}

		public virtual void Clear()
		{
			lock (_padlock)
			{
				TriggerClearEvent(TriggerTime.Before);

				_collection.Clear();

				TriggerClearEvent(TriggerTime.After);
			}
		}

		public virtual bool Contains(T item)
		{
			lock (_padlock)
			{
				return _collection.Contains(item);
			}
		}

		public virtual void CopyTo(T[] array, int arrayIndex)
		{
			lock (_padlock)
			{
				_collection.CopyTo(array, arrayIndex);
			}
		}

		public virtual int Count
		{
			get { lock (_padlock) return _collection.Count; }
		}

		public virtual long LongCount
		{
			get { lock (_padlock) return _collection.LongCount(); }
		}

		public virtual bool IsReadOnly
		{
			get { lock (_padlock) return _collection.IsReadOnly; }
		}

		public virtual bool Remove(T item)
		{
			lock (_padlock)
			{
				TriggerEvent(item, TriggerOp.Remove, TriggerTime.Before);
				try
				{
					return _collection.Remove(item);
				}
				finally
				{
					TriggerEvent(item, TriggerOp.Remove, TriggerTime.After); //always trigger remove no matter what!
				}
			}
		}

		public virtual IEnumerator<T> GetEnumerator()
		{
			lock (_padlock)
			{
				return _collection.GetEnumerator();
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public override string ToString()
		{
			lock (_padlock)
			{
				
				return FRIENDLY_NAME + ":" + _collection.Count;
			}
		}
	}
}
