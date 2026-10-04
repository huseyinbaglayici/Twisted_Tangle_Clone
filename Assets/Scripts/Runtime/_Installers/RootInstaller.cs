using Reflex.Core;
using Runtime.Boostrap;
using Runtime.Gameplay.Managers;
using UnityEngine;

namespace Runtime._Installers
{
    public class RootInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(new BoostrapReadyService());
            builder.RegisterValue(new GameplayReadyService());
        }
    }
}