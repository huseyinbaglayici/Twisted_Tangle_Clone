using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace Runtime.Boostrap
{
    public class SceneLoader : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;

        // Mobile defaults to 30 fps, which makes the 50 Hz rope simulation step unevenly per frame.
        [SerializeField] private int targetFrameRate = 144;

        private void Awake() => Application.targetFrameRate = targetFrameRate;

        private void Start() => _sceneFlow.Boot().Forget();
    }
}
