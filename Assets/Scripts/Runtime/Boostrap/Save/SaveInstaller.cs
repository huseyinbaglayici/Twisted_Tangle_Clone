using Reflex.Core;
using Runtime.Boostrap.Save.ConcreteTypes;
using UnityEngine;

namespace Runtime.Boostrap.Save
{
    public class SaveInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            var saveService = new SaveService();
            saveService.Register(new CurrencyStorage());
            saveService.Register(new HealthStorage());
            saveService.Register(new LevelIdProgression());

            builder.RegisterValue(saveService);
        }
    }
}