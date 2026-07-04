using Reflex.Core;
using Runtime.Boostrap;
using UnityEngine;

namespace Runtime._Installers
{
    public class RootInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(new BoostrapReadyService());
        }
    }
}