using System;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // Sound / haptics switches plus a home button. Tapping the dim background or the X closes it.
    [RequireComponent(typeof(UiPanelAnimator))]
    public class SettingsPopup : MonoBehaviour
    {
        [SerializeField] private SettingsToggle soundToggle;
        [SerializeField] private SettingsToggle hapticsToggle;
        [SerializeField] private Button homeButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimButton;

        private UiPanelAnimator _animator;
        private bool _listening;

        public event Action<bool> SoundChanged;
        public event Action<bool> HapticsChanged;
        public event Action HomeClicked;
        public event Action Closed;

        public bool IsVisible => Animator.IsVisible;

        private UiPanelAnimator Animator => _animator != null ? _animator : _animator = GetComponent<UiPanelAnimator>();

        public void Show(bool soundOn, bool hapticsOn)
        {
            Listen();
            soundToggle.Set(soundOn, false);
            hapticsToggle.Set(hapticsOn, false);
            Animator.Show();
        }

        public void Hide(bool instant = false) => Animator.Hide(instant);

        private void Close()
        {
            if (!IsVisible) return;
            Hide();
            Closed?.Invoke();
        }

        private void Listen()
        {
            if (_listening) return;
            _listening = true;
            soundToggle.Changed += on => SoundChanged?.Invoke(on);
            hapticsToggle.Changed += on => HapticsChanged?.Invoke(on);
            homeButton.onClick.AddListener(() => HomeClicked?.Invoke());
            closeButton.onClick.AddListener(Close);
            if (dimButton != null) dimButton.onClick.AddListener(Close);
        }
    }
}
