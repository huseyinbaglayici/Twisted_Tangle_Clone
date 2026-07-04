using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using UnityEngine;

namespace Runtime.MainMenu
{
    public class MenuLoadingService : MonoBehaviour
    {
        [Inject] private BoostrapReadyService _readyService;

        private async UniTaskVoid Start()
        {
            ShowLoadingScreen();
            await _readyService.WaitUntilReady();
            HideLoadingScreen();
        }

        private void ShowLoadingScreen()
        {
            Debug.LogWarning("Loading Screen Showing");
        }

        private void HideLoadingScreen()
        {
            Debug.LogWarning("Loading Screen OFF");
        }
    }
}