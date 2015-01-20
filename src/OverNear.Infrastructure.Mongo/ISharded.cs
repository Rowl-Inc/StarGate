using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.Infrastructure
{
    /// <summary>
    /// Collection is sharded
    /// </summary>
    public interface ISharded<T> : IDboEntity
    {
        T ShardKey { get; set; }
    }
}
