using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Runtime.Boostrap
{
    public class SceneLoader : MonoBehaviour
    {
        [Inject] private BoostrapReadyService _readyService;

        private async UniTaskVoid Start()
        {
            await _readyService.WaitUntilReady();
            await SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Additive);
        }
    }
}
