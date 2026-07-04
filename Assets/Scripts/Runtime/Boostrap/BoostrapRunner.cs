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
            _readyService.SetReady();
        }
    }
}