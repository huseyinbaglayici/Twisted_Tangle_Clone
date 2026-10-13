using System;
using Runtime.Gameplay.PowerUps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    public class PowerUpButton : MonoBehaviour
    {
        [SerializeField] private PowerUpType type;
        [SerializeField] private Button button;

        [SerializeField] private GameObject countBadge;
        [SerializeField] private TMP_Text countLabel;
        [SerializeField] private GameObject dimOverlay;
        [SerializeField] private GameObject selectedGlow;

        public event Action<PowerUpType> Clicked;

        public PowerUpType Type => type;

        private void Awake() => button.onClick.AddListener(() => Clicked?.Invoke(type));

        // clickable: can be requested now, or is the one waiting for a target (tap again cancels).
        // The count badge only shows on a clickable button.
        public void Set(int count, bool clickable, bool selected)
        {
            button.interactable = clickable;
            dimOverlay.SetActive(!clickable && !selected);
            selectedGlow.SetActive(selected);
            countLabel.text = count.ToString();
            countBadge.SetActive(clickable && !selected && count > 0);
        }
    }
}
