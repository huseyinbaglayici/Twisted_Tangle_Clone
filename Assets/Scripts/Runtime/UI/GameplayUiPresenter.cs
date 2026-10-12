using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Boostrap.Save;
using Runtime.Gameplay;
using UnityEngine;

namespace Runtime.UI
{
    public class GameplayUiPresenter : MonoBehaviour
    {
        [Inject] private SaveService _save;
        [Inject] private AudioPlayer _audio;

        [Header("Gameplay")]
        [SerializeField] private LevelFlowController flow;
        [SerializeField] private LevelTimer timer;

        [Header("Views")]
        [SerializeField] private GameplayHud hud;
        [SerializeField] private LevelCompletePanel completePanel;
        [SerializeField] private LevelFailPanel failPanel;
        [SerializeField] private SettingsPopup settings;

        [Tooltip("Win event -> complete panel. Lets the last merge pop and the confetti play first.")]
        [SerializeField] private float completePanelDelay = 0.7f;

        private Tween _pendingPanel;

        // Awake, not Start: the flow already starts the first level in its own Start.
        private void Awake()
        {
            flow.LevelStarted += OnLevelStarted;
            flow.LevelWon += OnLevelWon;
            flow.LevelFailed += OnLevelFailed;
            timer.SecondsChanged += hud.SetTime;

            hud.RetryClicked += OnRetryClicked;
            hud.SettingsClicked += OnSettingsClicked;
            completePanel.ContinueClicked += OnContinueClicked;
            failPanel.RetryClicked += OnFailRetryClicked;
            failPanel.HomeClicked += OnHomeClicked;
            settings.HomeClicked += OnHomeClicked;
            settings.SoundChanged += OnSoundChanged;
            settings.HapticsChanged += OnHapticsChanged;
            settings.Closed += OnSettingsClosed;
        }

        private void Start()
        {
            completePanel.Hide(true);
            failPanel.Hide(true);
            settings.Hide(true);
            hud.Show();
        }

        private void OnDestroy()
        {
            _pendingPanel?.Kill();
            flow.LevelStarted -= OnLevelStarted;
            flow.LevelWon -= OnLevelWon;
            flow.LevelFailed -= OnLevelFailed;
            timer.SecondsChanged -= hud.SetTime;
        }

        private void OnLevelStarted(int level)
        {
            _pendingPanel?.Kill();
            hud.SetLevel(level);
            if (completePanel.IsVisible) completePanel.Hide();
            if (failPanel.IsVisible) failPanel.Hide();
        }

        private void OnLevelWon(int level)
        {
            _pendingPanel?.Kill();
            _pendingPanel = DOVirtual.DelayedCall(completePanelDelay, completePanel.Show)
                .SetLink(gameObject);
        }

        private void OnLevelFailed(int level) => failPanel.Show(level);

        private void OnContinueClicked()
        {
            PlayClick();
            flow.ExitToMenu();
        }

        private void OnFailRetryClicked()
        {
            PlayClick();
            flow.Retry();
        }

        private void OnRetryClicked()
        {
            PlayClick();
            flow.Retry();
        }

        private void OnSettingsClicked()
        {
            PlayClick();
            settings.Show(_audio.SoundOn, _save.HapticsOn);
            timer.Frozen = true;
        }

        private void OnSettingsClosed()
        {
            timer.Frozen = false;
            PlayClick();
        }

        private void OnHomeClicked()
        {
            PlayClick();
            flow.ExitToMenu();
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
    }
}
