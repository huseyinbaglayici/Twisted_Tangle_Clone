using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using Runtime.Gameplay.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Runtime.MainMenu
{
    public class MenuLoadingService : MonoBehaviour
    {
        [Inject] private BoostrapReadyService _readyService;
        [Inject] private GameplayReadyService _gameplayReadyService;
        [SerializeField] private RectTransform panelTransform;
        [SerializeField] private Button playButton;

        private async UniTaskVoid Start()
        {
            playButton.onClick.AddListener(() => LoadGameplay().Forget());

            ShowLoadingScreen();
            await _readyService.WaitUntilReady();
            await UniTask.Delay(1500);
            HideLoadingScreen();
        }

        private async UniTask LoadGameplay()
        {
            _gameplayReadyService.Reset();
            ShowLoadingScreen();
            await SceneManager.LoadSceneAsync("Gameplay", LoadSceneMode.Additive);
            await _gameplayReadyService.WaitUntilReady();
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