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

        #region Parent Holders

        [SerializeField] private Transform pinParent;
        [SerializeField] private Transform ropeParent;
        [SerializeField] private Transform gridCellParent;

        #endregion


        public void InstallBindings(ContainerBuilder builder)
        {
            var registry = new PoolRegistry();

            CreateAndRegisterPool(() => Instantiate(gridCellPrefab), gridCellParent, registry, 65);
            CreateAndRegisterPool(() => Instantiate(pinPrefab), pinParent, registry, 50);
            CreateAndRegisterPool(() => Instantiate(ropePrefab), ropeParent, registry, 40);

            builder.RegisterValue(registry);
        }

        // this method prewarms & register pool to the registry
        private void CreateAndRegisterPool<T>(Func<T> factory, Transform parent, PoolRegistry registry,
            int preWarmValue = 50) where T : Component, IPoolable
        {
            var pool = new PoolService<T>(factory, parent);
            pool.Prewarm(preWarmValue);
            registry.Register(pool);
        }
    }
}