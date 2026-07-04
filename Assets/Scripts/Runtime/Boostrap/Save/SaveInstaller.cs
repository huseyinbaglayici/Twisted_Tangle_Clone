using Reflex.Core;
using Runtime.Boostrap.Save.ConcreteTypes;
using UnityEngine;

namespace Runtime.Boostrap.Save
{
    public class SaveInstaller : MonoBehaviour, IInstaller
    {
        #region Savable Data Types

        private LevelIdProgression _levelIDValue;
        private CurrencyStorage _currencyValue;
        private HealthStorage _healthValue;

        #endregion

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