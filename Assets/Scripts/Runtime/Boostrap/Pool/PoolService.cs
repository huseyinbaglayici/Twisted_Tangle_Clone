using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    // Active-object tracking, DespawnAll, re-parent on despawn, transform/tween reset and a
    // double-despawn guard.
    public class PoolService<T> where T : Component, IPoolable
    {
        private readonly Queue<T> _inactive = new();
        private readonly List<T> _active = new();
        private readonly HashSet<T> _activeSet = new();
        private readonly Func<T> _factory;
        private readonly Transform _parent;

        public int ActiveCount => _active.Count;
        public IReadOnlyList<T> Active => _active;

        public PoolService(Func<T> factory, Transform parent)
        {
            _factory = factory;
            _parent = parent;
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
                _inactive.Enqueue(CreateNew());
        }

        public T Dequeue(Transform parent = null)
        {
            var obj = _inactive.Count > 0 ? _inactive.Dequeue() : CreateNew();

            var t = obj.transform;
            t.SetParent(parent != null ? parent : _parent, false);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            _active.Add(obj);
            _activeSet.Add(obj);
            obj.gameObject.SetActive(true);
            obj.Spawn();
            return obj;
        }

        public void Enqueue(T obj)
        {
            if (obj == null || !_activeSet.Remove(obj))
                return; // already in pool (merge + level reset can hit the same object in one frame)

            _active.Remove(obj);
            obj.transform.DOKill();
            obj.Despawn();
            obj.gameObject.SetActive(false);
            obj.transform.SetParent(_parent, false);
            _inactive.Enqueue(obj);
        }

        public void DespawnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                Enqueue(_active[i]);
        }

        private T CreateNew()
        {
            var obj = _factory();
            obj.transform.SetParent(_parent, false);
            obj.gameObject.SetActive(false);
            return obj;
        }
    }
}
