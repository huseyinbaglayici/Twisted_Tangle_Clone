using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    [RequireComponent(typeof(UiPanelAnimator))]
    public class LevelFailPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button homeButton;

        private UiPanelAnimator _animator;
        private bool _listening;

        public event Action RetryClicked;
        public event Action HomeClicked;

        public bool IsVisible => Animator.IsVisible;

        private UiPanelAnimator Animator => _animator != null ? _animator : _animator = GetComponent<UiPanelAnimator>();

        public void Show(int failedLevel)
        {
            Listen();
            levelLabel.text = $"Level {failedLevel}";
            Animator.Show();
        }

        public void Hide(bool instant = false) => Animator.Hide(instant);

        private void Listen()
        {
            if (_listening) return;
            _listening = true;
            retryButton.onClick.AddListener(() => RetryClicked?.Invoke());
            homeButton.onClick.AddListener(() => HomeClicked?.Invoke());
        }
    }
}
