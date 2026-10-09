using System;
using System.Collections.Generic;
using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Effects
{
    // Single entry point for particle effects. Several prefabs share the FxEntity type, so I keep one
    // pool per FxType instead of going through the type-keyed PoolRegistry.
    public class FxPlayer : MonoBehaviour
    {
        [Serializable]
        private class Entry
        {
            public FxType type;
            public FxEntity prefab;
            public int prewarm = 2;
        }

        [SerializeField] private List<Entry> effects = new();

        private readonly Dictionary<FxType, PoolService<FxEntity>> _pools = new();

        private void Awake()
        {
            foreach (var entry in effects)
            {
                if (entry.prefab == null || _pools.ContainsKey(entry.type))
                {
                    Debug.LogWarning($"{nameof(FxPlayer)}: '{entry.type}' has no prefab or is listed twice, skip.");
                    continue;
                }

                var holder = new GameObject($"{entry.type}Holder").transform;
                holder.SetParent(transform, false);

                var prefab = entry.prefab;
                PoolService<FxEntity> pool = null;
                pool = new PoolService<FxEntity>(() =>
                {
                    var fx = Instantiate(prefab);
                    fx.Bind(pool);
                    return fx;
                }, holder);
                pool.Prewarm(entry.prewarm);
                _pools[entry.type] = pool;
            }
        }

        // Tint overrides the start colour of the prefab's tint targets (e.g. merge pop = rope colour).
        public void Play(FxType type, Vector3 position, Color? tint = null)
        {
            if (!_pools.TryGetValue(type, out var pool))
            {
                Debug.LogWarning($"{nameof(FxPlayer)}: no pool for '{type}'.");
                return;
            }

            pool.Dequeue().Play(position, tint);
        }

        // Call on level unload / retry / next level.
        public void DespawnAll()
        {
            foreach (var pool in _pools.Values)
                pool.DespawnAll();
        }
    }
}
