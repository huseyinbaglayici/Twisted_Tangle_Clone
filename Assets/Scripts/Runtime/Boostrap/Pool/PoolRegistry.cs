using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    public class PoolRegistry
    {
        private readonly Dictionary<Type, object> _pools = new();
        private readonly List<Action> _despawnAll = new();

        public void Register<T>(PoolService<T> pool) where T : Component, IPoolable
        {
            _pools[typeof(T)] = pool;
            _despawnAll.Add(pool.DespawnAll);
        }

        public PoolService<T> Get<T>() where T : Component, IPoolable
        {
            return (PoolService<T>)_pools[typeof(T)];
        }

        // Call on level unload / retry / next level.
        public void DespawnAll()
        {
            foreach (var despawn in _despawnAll)
                despawn();
        }
    }
}
