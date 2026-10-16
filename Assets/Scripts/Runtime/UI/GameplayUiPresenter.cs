using System.Collections.Generic;
using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Boostrap.Save;
using Runtime.Gameplay;
using Runtime.Gameplay.PowerUps;
using Runtime.Level;
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
        [SerializeField] private PowerUpController powerUps;

        [Header("Views")]
        [SerializeField] private GameplayHud hud;
        [SerializeField] private LevelCompletePanel completePanel;
        [SerializeField] private LevelFailPanel failPanel;
        [SerializeField] private SettingsPopup settings;
        [SerializeField] private PowerUpPopup powerUpPopup;
        [SerializeField] private FeatureIntroPopup featureIntro;
        [SerializeField] private DifficultyBanner difficultyBanner;

        [Header("Difficulty")]
        [SerializeField] private DifficultyStyle difficultyStyle;

        [Tooltip("Win event -> complete panel. Lets the last merge pop and the confetti play first.")]
        [SerializeField] private float completePanelDelay = 0.7f;

        private Tween _pendingPanel;
        private Tween _pendingIntro;
        private readonly Queue<LevelFeature> _intros = new();
        private DifficultyTint[] _tints;

        // Awake, not Start: the flow already starts the first level in its own Start.
        private void Awake()
        {
            _tints = GetComponentsInChildren<DifficultyTint>(true);
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

            foreach (var button in hud.PowerUps) button.Clicked += OnPowerUpClicked;
            powerUpPopup.ActionClicked += OnPowerUpConfirmed;
            featureIntro.Closed += OnFeatureIntroClosed;
            powerUps.Changed += RefreshPowerUps;
        }

        private void Start()
        {
            completePanel.Hide(true);
            failPanel.Hide(true);
            settings.Hide(true);
            powerUpPopup.Hide(true);
            featureIntro.Hide(true);
            hud.Show();
            RefreshPowerUps();
        }

        private void OnDestroy()
        {
            _pendingPanel?.Kill();
            _pendingIntro?.Kill();
            flow.LevelStarted -= OnLevelStarted;
            flow.LevelWon -= OnLevelWon;
            flow.LevelFailed -= OnLevelFailed;
            timer.SecondsChanged -= hud.SetTime;
            powerUps.Changed -= RefreshPowerUps;
        }

        private void OnLevelStarted(int level)
        {
            _pendingPanel?.Kill();
            hud.SetLevel(level);
            var theme = difficultyStyle.For(flow.CurrentDifficulty);
            foreach (var tint in _tints) tint.Apply(theme);
            difficultyBanner.Play(theme);
            if (completePanel.IsVisible) completePanel.Hide();
            if (failPanel.IsVisible) failPanel.Hide();
            QueueFeatureIntros(theme != null ? difficultyBanner.Duration : 0f);
        }

        // Every feature the level brings for the first time gets its intro, one after another, once the difficulty
        // banner is gone. The clock waits while they are open; a feature counts as seen once its intro is closed.
        private void QueueFeatureIntros(float delay)
        {
            _pendingIntro?.Kill();
            _intros.Clear();
            if (featureIntro.IsVisible) featureIntro.Hide(true);

            foreach (var feature in flow.CurrentFeatures)
                if (featureIntro.Has(feature) && !_save.IsTutorialShown(feature.ToString())) _intros.Enqueue(feature);
            if (_intros.Count == 0)
            {
                timer.Paused = false;
                return;
            }

            timer.Paused = true;
            _pendingIntro = DOVirtual.DelayedCall(delay, ShowNextIntro).SetLink(gameObject);
        }

        private void ShowNextIntro()
        {
            if (_intros.Count == 0)
            {
                timer.Paused = false;
                return;
            }
            featureIntro.Show(_intros.Peek());
        }

        private void OnFeatureIntroClosed()
        {
            PlayClick();
            if (_intros.Count > 0) _save.MarkTutorialShown(_intros.Dequeue().ToString());
            ShowNextIntro();
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
            timer.Paused = true;
        }

        private void OnSettingsClosed()
        {
            timer.Paused = false;
            PlayClick();
        }

        private void OnHomeClicked()
        {
            PlayClick();
            flow.ExitToMenu();
        }

        // The popup explains a power-up the first time only; after that it starts straight away
        // (the scissors still wait for a rope tap).
        private void OnPowerUpClicked(PowerUpType type)
        {
            PlayClick();
            powerUps.Toggle(type);
            if (powerUps.State != PowerUpState.Requested) return;

            string key = type.ToString();
            if (_save.IsTutorialShown(key))
            {
                powerUps.Confirm();
                return;
            }

            _save.MarkTutorialShown(key);
            powerUpPopup.Show(type);
        }

        private void OnPowerUpConfirmed()
        {
            PlayClick();
            powerUps.Confirm();
        }

        private void RefreshPowerUps()
        {
            bool requested = powerUps.State == PowerUpState.Requested;
            foreach (var button in hud.PowerUps)
            {
                var type = button.Type;
                bool selected = powerUps.IsInUse && powerUps.Active == type;
                bool clickable = powerUps.CanRequest(type) || (requested && selected);
                button.Set(powerUps.Count(type), clickable, selected);
            }

            if (!requested && powerUpPopup.IsVisible) powerUpPopup.Hide();
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
