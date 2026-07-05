using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using UnityEngine;

namespace Runtime.MainMenu
{
    public class MenuLoadingService : MonoBehaviour
    {
        [Inject] private BoostrapReadyService _readyService;
        [SerializeField] private RectTransform panelTransform;

        private async UniTaskVoid Start()
        {
            ShowLoadingScreen();
            await _readyService.WaitUntilReady();
            await UniTask.Delay(1500);
            HideLoadingScreen();
        }

        private void ShowLoadingScreen()
        {
            if (panelTransform.gameObject.activeSelf == false)
                panelTransform.gameObject.SetActive(true);
        }

        private void HideLoadingScreen()
        {
            panelTransform.gameObject.SetActive(false);
        }
    }
}