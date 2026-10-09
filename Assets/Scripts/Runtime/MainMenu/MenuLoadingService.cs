using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.MainMenu
{
    public class MenuLoadingService : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;
        [SerializeField] private RectTransform panelTransform;
        [SerializeField] private Button playButton;

        private async UniTaskVoid Start()
        {
            playButton.onClick.AddListener(() => LoadGameplay().Forget());

            ShowLoadingScreen();
            // Play can unload this scene before the delay ends; the panel is gone by then.
            if (await UniTask.Delay(1500, cancellationToken: destroyCancellationToken).SuppressCancellationThrow()) return;
            HideLoadingScreen();
        }

        // The menu scene is unloaded once the level is ready, taking the loading panel with it.
        private async UniTask LoadGameplay()
        {
            playButton.interactable = false;
            ShowLoadingScreen();
            await _sceneFlow.LoadGameplay();
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