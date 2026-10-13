using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    public class PowerUpButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private GameObject icon;

        [Header("Indicators")]
        [SerializeField] private GameObject countBadge;
        [SerializeField] private TMP_Text countLabel;
        [SerializeField] private GameObject plusBadge;

        [Header("States")]
        [SerializeField] private GameObject lockedRoot;
        [SerializeField] private TMP_Text unlockLevelLabel;
        [SerializeField] private GameObject dimOverlay;
        [SerializeField] private GameObject selectedGlow;
        [SerializeField] private Color lockedBackground = new(0.62f, 0.66f, 0.72f);

        [SerializeField] private int defaultCount = 3;

        private Color _normalBackground;

        public event Action Clicked;

        public int Count { get; private set; }
        public bool Locked { get; private set; }
        public bool Dimmed { get; private set; }
        public bool Selected { get; private set; }

        private void Awake()
        {
            _normalBackground = background.color;
            button.onClick.AddListener(() => Clicked?.Invoke());
            Count = defaultCount;
            Refresh();
        }

        public void SetCount(int count)
        {
            Count = Mathf.Max(0, count);
            Refresh();
        }

        public void SetLocked(bool locked, int unlockLevel = 0)
        {
            Locked = locked;
            unlockLevelLabel.text = $"Lv {unlockLevel}";
            Refresh();
        }

        // Another power-up is in use, or this one can't be used right now.
        public void SetDimmed(bool dimmed)
        {
            Dimmed = dimmed;
            Refresh();
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            Refresh();
        }

        private void Refresh()
        {
            bool clickable = !Locked && !Dimmed;
            button.interactable = clickable;
            background.color = Locked ? lockedBackground : _normalBackground;
            icon.SetActive(!Locked);
            lockedRoot.SetActive(Locked);
            dimOverlay.SetActive(Dimmed && !Locked);
            selectedGlow.SetActive(Selected && !Locked);
            countLabel.text = Count.ToString();
            countBadge.SetActive(clickable && Count > 0);
            plusBadge.SetActive(clickable && Count == 0);
        }
    }
}
