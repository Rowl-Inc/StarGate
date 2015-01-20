using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.Infrastructure
{
    public interface IShardedById<T> : IDboEntity
    {
        T Id { get; set; }
    }
}
