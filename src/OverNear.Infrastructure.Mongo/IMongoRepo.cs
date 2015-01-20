using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

using MongoDB.Driver;

namespace OverNear.Infrastructure
{
    public interface IMongoRepo
    {
        MongoServer Server { get; }
        MongoDatabase Database { get; }
    }

    public interface IMongoRepo<T> : IMongoRepo
    {
        event Action<IMongoRepo<T>> SingletonInit;

        MongoCollection<T> Collection { get; }
        MongoCollectionSettings Settings { get; }
    }
}
