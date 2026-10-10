using Cysharp.Threading.Tasks;
using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Boostrap;
using Runtime.Boostrap.Save;
using Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.MainMenu
{
    // Home screen: Play -> gameplay, the gear opens the shared settings popup. The menu pieces pop in once
    // the loading curtain starts lifting, so the player sees them arrive instead of a static frame.
    public class MainMenuPresenter : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;
        [Inject] private SaveService _save;
        [Inject] private AudioPlayer _audio;

        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private SettingsPopup settings;

        [Header("Intro")]
        [SerializeField] private RectTransform logo;
        [SerializeField] private float logoDrop = 260f;
        [SerializeField] private RectTransform[] pathNodes;
        [SerializeField] private float nodeStagger = 0.06f;
        [SerializeField] private float showDuration = 0.45f;
        [Tooltip("Soft glow behind Play, breathing in and out once the intro is done.")]
        [SerializeField] private Graphic playGlow;

        private void Awake()
        {
            playButton.onClick.AddListener(OnPlayClicked);
            settingsButton.onClick.AddListener(OnSettingsClicked);
            settings.SoundChanged += OnSoundChanged;
            settings.HapticsChanged += OnHapticsChanged;
            settings.Closed += PlayClick;
        }

        private void Start()
        {
            settings.Hide(true);
            PlayIntro().Forget();
        }

        private void OnPlayClicked()
        {
            if (_sceneFlow.IsBusy) return;
            PlayClick();
            playButton.interactable = false;
            _sceneFlow.LoadGameplay().Forget();
        }

        private void OnSettingsClicked()
        {
            PlayClick();
            settings.Show(_audio.SoundOn, _save.HapticsOn);
        }

        private void OnSoundChanged(bool on)
        {
            _audio.SoundOn = on;
            PlayClick();
        }

        private void OnHapticsChanged(bool on)
        {
            _save.HapticsOn = on;
            PlayClick();
        }

        private void PlayClick() => _audio.Play(SfxType.ButtonClick);

        // ---------- intro ----------
        private async UniTaskVoid PlayIntro()
        {
            var logoPosition = logo.anchoredPosition;
            logo.anchoredPosition = logoPosition + Vector2.up * logoDrop;
            SetScale(Vector3.zero);

            var token = this.GetCancellationTokenOnDestroy();
            await UniTask.WaitUntil(() => !_sceneFlow.IsBusy && !_sceneFlow.IsScreenCovered, cancellationToken: token);

            var intro = DOTween.Sequence().SetLink(gameObject);
            intro.Join(logo.DOAnchorPos(logoPosition, showDuration).SetEase(Ease.OutBack));
            for (int i = 0; i < pathNodes.Length; i++)
                intro.Insert(0.1f + i * nodeStagger, pathNodes[i].DOScale(1f, 0.3f).SetEase(Ease.OutBack));
            intro.Insert(0.2f, playButton.transform.DOScale(1f, showDuration).SetEase(Ease.OutBack));
            intro.Insert(0.25f, settingsButton.transform.DOScale(1f, showDuration).SetEase(Ease.OutBack));
            if (playGlow != null)
                intro.OnComplete(() => playGlow.DOFade(0f, 1f).SetEase(Ease.InOutQuad).SetLoops(-1, LoopType.Yoyo)
                    .SetLink(gameObject));
        }

        private void SetScale(Vector3 scale)
        {
            foreach (var node in pathNodes) node.localScale = scale;
            playButton.transform.localScale = scale;
            settingsButton.transform.localScale = scale;
        }
    }
}
