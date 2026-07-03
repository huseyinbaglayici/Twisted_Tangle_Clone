using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    public class PoolService<T> where T : Component, IPoolable
    {
        private Queue<T> _poolables = new Queue<T>();

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
                return CreateNew();
            }

            dequeuedObject.Spawn();
            return dequeuedObject;
        }

        private T CreateNew()
        {
            var obj = new GameObject(typeof(T).Name);
            return obj.AddComponent<T>();
        }

        public void Enqueue(T obj)
        {
            obj.Despawn();
            _poolables.Enqueue(obj);
        }
    }
}