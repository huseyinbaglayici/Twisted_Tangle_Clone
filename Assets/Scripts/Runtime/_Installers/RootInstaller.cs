using Reflex.Core;
using Reflex.Enums;
using Runtime.Boostrap.Pool;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace Runtime._Installers
{
    public class RootInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterType(typeof(PoolRegistry), Lifetime.Singleton, Resolution.Eager);
        }
    }
}