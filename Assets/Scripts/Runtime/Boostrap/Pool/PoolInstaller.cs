using System;
using Reflex.Core;
using Runtime.Entities;
using UnityEngine;

namespace Runtime.Boostrap.Pool
{
    public class PoolInstaller : MonoBehaviour, IInstaller
    {
        #region Entity Types

        [SerializeField] private PinEntity pinPrefab;
        [SerializeField] private RopeEntity ropePrefab;
        [SerializeField] private GridCellEntity gridCellPrefab;

        #endregion

        #region Entity Name Constants

        private const string PinHolder = "PinHolder";
        private const string RopeHolder = "RopeHolder";
        private const string GridCellHolder = "GridCellHolder";

        #endregion

        // Pool Creation Order --> GridCell - Rope - Pin 
        public void InstallBindings(ContainerBuilder builder)
        {
            var pinParent = CreateParents(PinHolder);
            var ropeParent = CreateParents(RopeHolder);
            var gridCellParent = CreateParents(GridCellHolder);

            var registry = new PoolRegistry();
            TryCreateAndRegisterPool(gridCellPrefab, gridCellParent, registry, 65);
            TryCreateAndRegisterPool(ropePrefab, ropeParent, registry, 60);
            TryCreateAndRegisterPool(pinPrefab, pinParent, registry, 50);

            builder.RegisterValue(registry);
        }

        private Transform CreateParents(string holderName)
        {
            return new GameObject(holderName).transform;
        }

        // this method prewarms & register pool to the registry

        private void TryCreateAndRegisterPool<T>(T prefab, Transform parent, PoolRegistry registry, int prewarmValue)
            where T : Component, IPoolable
        {
            if (prefab == null)
            {
                Debug.LogWarning($"{nameof(PoolInstaller)}: '{typeof(T).Name}' prefab couldn't assigned, skip.");
                return;
            }

            CreateAndRegisterPool(() => Instantiate(prefab), parent, registry, prewarmValue);
        }

        private void CreateAndRegisterPool<T>(Func<T> factory, Transform parent, PoolRegistry registry,
            int preWarmValue = 50) where T : Component, IPoolable
        {
            var pool = new PoolService<T>(factory, parent);
            pool.Prewarm(preWarmValue);
            registry.Register(pool);
        }
    }
}