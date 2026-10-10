using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace Runtime.Boostrap
{
    // Entry point of the Boostrap scene: opens the main menu behind the splash.
    public class SceneLoader : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;

        private void Start() => _sceneFlow.Boot().Forget();
    }
}
