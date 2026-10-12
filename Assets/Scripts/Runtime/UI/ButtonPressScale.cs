using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Runtime.UI
{
    // Scales a child pivot so the button's hit area stays put.
    public class ButtonPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private float pressedScale = 0.9f;
        [SerializeField] private float pressDuration = 0.08f;
        [SerializeField] private float releaseDuration = 0.25f;

        private Selectable _selectable;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            if (target == null) target = (RectTransform)transform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;
            target.DOKill();
            target.DOScale(pressedScale, pressDuration).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            target.DOKill();
            target.DOScale(1f, releaseDuration).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
        }

        private void OnDisable()
        {
            if (target == null) return;
            target.DOKill();
            target.localScale = Vector3.one;
        }
    }
}
