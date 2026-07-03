using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    public class PoolService<T> where T : Component, IPoolable
    {
        private Queue<T> _poolables = new Queue<T>();
        private Func<T> _factory;
        private readonly Transform _parent;

        public PoolService(Func<T> factory, Transform parent)
        {
            _factory = factory;
            _parent = parent;
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _poolables.Enqueue(CreateNew());
            }
        }

        public T Dequeue()
        {
            _poolables.TryDequeue(out var dequeuedObject);
            if (EqualityComparer<T>.Default.Equals(dequeuedObject, default))
            {
                var obj = CreateNew();
                obj.Spawn();
                return obj;
            }

            dequeuedObject.Spawn();
            return dequeuedObject;
        }

        private T CreateNew()
        {
            var obj = _factory();
            obj.transform.SetParent(_parent);
            obj.gameObject.SetActive(false);
            return obj;
        }

        public void Enqueue(T obj)
        {
            obj.Despawn();
            obj.gameObject.SetActive(false);
            _poolables.Enqueue(obj);
        }
    }
}