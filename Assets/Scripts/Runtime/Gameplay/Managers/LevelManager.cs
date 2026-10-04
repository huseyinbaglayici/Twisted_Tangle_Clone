using Reflex.Attributes;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay.Managers
{
    // Temporary until LevelFlowController: draws one level and signals ready.
    public class LevelManager : MonoBehaviour
    {
        [Inject] private GameplayReadyService _readyService;

        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private int levelNumber = 1;

        private void Start()
        {
            levelBuilder.Build(new LevelRepository().Load(levelNumber));
            _readyService.SetReady();
        }
    }
}
