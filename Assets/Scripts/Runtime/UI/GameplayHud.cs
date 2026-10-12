using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    public class GameplayHud : MonoBehaviour
    {
        [Header("Level")]
        [SerializeField] private TMP_Text levelNumberLabel;

        [Header("Time")]
        [SerializeField] private GameObject timeRoot;
        [SerializeField] private TMP_Text timeLabel;
        [SerializeField] private RectTransform alertTarget;
        [SerializeField] private Color timeColor = Color.white;
        [SerializeField] private Color alertColor = new(1f, 0.38f, 0.32f);
        [Tooltip("A single pulse when the clock reaches each of these; the last one keeps pulsing every second.")]
        [SerializeField] private int[] warnAtSeconds = { 30, 20, 10 };

        [Header("Buttons")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button settingsButton;

        [Header("Intro")]
        [SerializeField] private UiPanelAnimator header;
        [SerializeField] private UiPanelAnimator footer;

        private int _shownSeconds = -1;

        public event Action RetryClicked;
        public event Action SettingsClicked;

        private void Awake()
        {
            retryButton.onClick.AddListener(() => RetryClicked?.Invoke());
            settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
        }

        public void Show()
        {
            if (header != null) header.Show();
            if (footer != null) footer.Show();
        }

        public void SetLevel(int level) => levelNumberLabel.text = level.ToString();

        public void SetTime(int seconds)
        {
            timeRoot.SetActive(seconds >= 0);
            if (seconds < 0 || seconds == _shownSeconds)
            {
                _shownSeconds = seconds;
                return;
            }

            bool ticking = _shownSeconds >= 0 && seconds < _shownSeconds;
            _shownSeconds = seconds;
            timeLabel.text = seconds.ToString();
            int last = warnAtSeconds.Length > 0 ? warnAtSeconds[^1] : 0;
            timeLabel.color = seconds <= last ? alertColor : timeColor;
            if (ticking && (seconds <= last || Array.IndexOf(warnAtSeconds, seconds) >= 0)) Pulse();
        }

        private void Pulse()
        {
            if (alertTarget == null) return;
            alertTarget.DOKill(true);
            alertTarget.DOPunchScale(Vector3.one * 0.25f, 0.3f, 6, 0.5f).SetLink(gameObject);
        }
    }
}
