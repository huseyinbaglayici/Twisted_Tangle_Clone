using System;
using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap.Save;
using Runtime.Boostrap.Save.ConcreteTypes;
using UnityEngine;

namespace Runtime.Boostrap
{
    public class BoostrapRunner : MonoBehaviour
    {
        [Inject] private SaveService _saveService;
        [Inject] private BoostrapReadyService _readyService;

        private void Start()
        {
            int currency = _saveService.Get<CurrencyStorage>().Load();
            byte health = _saveService.Get<HealthStorage>().Load();
            int levelID = _saveService.Get<LevelIdProgression>().Load();

            _readyService.SetReady();
        }
    }
}