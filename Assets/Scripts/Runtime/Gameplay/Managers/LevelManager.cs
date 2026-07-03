using Reflex.Attributes;
using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Gameplay.Managers
{
    public class LevelManager : MonoBehaviour
    {
        [Inject] private PoolRegistry _poolRegistry;

        private void Start()
        {
            Debug.LogWarning(_poolRegistry != null ? "Debug success" : "Debug failed");
        }
    }
}