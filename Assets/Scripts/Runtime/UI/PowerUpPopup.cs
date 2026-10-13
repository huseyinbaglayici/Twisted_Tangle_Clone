using System;
using Runtime.Gameplay.PowerUps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // Shown while a power-up waits for its target or confirm. The dim leaves a hole over the board, so ropes stay
    // visible and tappable; the scissors have no confirm button, the rope tap starts them.
    [RequireComponent(typeof(UiPanelAnimator))]
    public class PowerUpPopup : MonoBehaviour
    {
        [Serializable]
        private class Entry
        {
            public PowerUpType type;
            public Sprite icon;
            public string title;
            [TextArea] public string description;
            [Tooltip("Confirm button label; empty = no button.")]
            public string action;
        }

        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabel;
        [SerializeField] private Entry[] entries;

        private UiPanelAnimator _animator;

        public event Action ActionClicked;

        public bool IsVisible => Animator.IsVisible;

        private UiPanelAnimator Animator => _animator != null ? _animator : _animator = GetComponent<UiPanelAnimator>();

        private void Awake() => actionButton.onClick.AddListener(() => ActionClicked?.Invoke());

        public void Show(PowerUpType type)
        {
            var entry = Array.Find(entries, e => e.type == type);
            if (entry == null) return;
            icon.sprite = entry.icon;
            title.text = entry.title;
            description.text = entry.description;
            actionLabel.text = entry.action;
            actionButton.gameObject.SetActive(!string.IsNullOrEmpty(entry.action));
            Animator.Show();
        }

        public void Hide(bool instant = false) => Animator.Hide(instant);
    }
}
