using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // On/off switch: the knob slides to the right and the track turns green when on.
    [RequireComponent(typeof(Button))]
    public class SettingsToggle : MonoBehaviour
    {
        [SerializeField] private Image track;
        [SerializeField] private RectTransform knob;
        [SerializeField] private Color onColor = new(0.33f, 0.8f, 0.25f);
        [SerializeField] private Color offColor = new(0.42f, 0.4f, 0.55f);
        [SerializeField] private float knobTravel = 38f;
        [SerializeField] private float duration = 0.15f;

        private bool _listening;

        public event Action<bool> Changed;

        public bool IsOn { get; private set; }

        public void Set(bool on, bool animate)
        {
            Listen();
            IsOn = on;
            var x = on ? knobTravel : -knobTravel;
            var color = on ? onColor : offColor;
            knob.DOKill();
            track.DOKill();

            if (!animate)
            {
                knob.anchoredPosition = new Vector2(x, knob.anchoredPosition.y);
                track.color = color;
                return;
            }

            knob.DOAnchorPosX(x, duration).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);
            track.DOColor(color, duration).SetUpdate(true).SetLink(gameObject);
        }

        private void Listen()
        {
            if (_listening) return;
            _listening = true;
            GetComponent<Button>().onClick.AddListener(() =>
            {
                Set(!IsOn, true);
                Changed?.Invoke(IsOn);
            });
        }
    }
}
