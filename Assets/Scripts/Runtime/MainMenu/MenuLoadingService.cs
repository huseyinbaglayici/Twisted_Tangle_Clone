using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.MainMenu
{
    // Play button -> gameplay. The loading screen itself is the session-wide fader, so the menu's own
    // loading panel is only kept hidden.
    public class MenuLoadingService : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;
        [SerializeField] private RectTransform panelTransform;
        [SerializeField] private Button playButton;

        private void Start()
        {
            if (panelTransform != null) panelTransform.gameObject.SetActive(false);
            playButton.onClick.AddListener(() => LoadGameplay().Forget());
        }

        private async UniTask LoadGameplay()
        {
            if (_sceneFlow.IsBusy) return;
            playButton.interactable = false;
            await _sceneFlow.LoadGameplay();
        }
    }
}
