using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    public class PoolRegistry
    {
        private readonly Dictionary<Type, object> _pools = new();

        public void Register<T>(PoolService<T> pool) where T : Component, IPoolable
        {
            _pools[typeof(T)] = pool;
        }

        public PoolService<T> Get<T>() where T : Component, IPoolable
        {
            return (PoolService<T>)_pools[typeof(T)];
        }
    }
}