using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Runtime.MainMenu
{
    public class RubberBandDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private float damping = 0.3f;
        [SerializeField] private float maxOffset = 50f;
        [SerializeField] private float returnDuration = 0.3f;

        private RectTransform _rectTransform;
        private Vector2 _originalAnchoredPosition;
        private Tween _returnTween;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _originalAnchoredPosition = _rectTransform.anchoredPosition;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _returnTween?.Kill();
        }

        public void OnDrag(PointerEventData eventData)
        {
            var newY = _rectTransform.anchoredPosition.y + eventData.delta.y * damping;
            newY = Mathf.Clamp(newY, _originalAnchoredPosition.y - maxOffset, _originalAnchoredPosition.y + maxOffset);
            _rectTransform.anchoredPosition = new Vector2(_originalAnchoredPosition.x, newY);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _returnTween = _rectTransform.DOAnchorPos(_originalAnchoredPosition, returnDuration).SetEase(Ease.OutBack);
        }
    }
}
