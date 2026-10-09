using DG.Tweening;
using UnityEngine;

namespace Runtime.UI
{
    // Shared show/hide tween: the group fades, the content pops in with a scale (or slides in when slideOffset is set).
    // Hidden panels are deactivated so they cost nothing and never block raycasts.
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPanelAnimator : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private float fadeDuration = 0.25f;
        [SerializeField] private float showDuration = 0.45f;
        [SerializeField] private float hideDuration = 0.2f;
        [SerializeField] private float startScale = 0.6f;
        [Tooltip("Non-zero = slide the content in from this offset instead of scaling it.")]
        [SerializeField] private Vector2 slideOffset;
        [SerializeField] private Ease showEase = Ease.OutBack;
        [SerializeField] private Ease hideEase = Ease.InBack;

        private CanvasGroup _group;
        private Vector2 _contentPosition;
        private Sequence _sequence;
        private bool _initialized;

        public bool IsVisible { get; private set; }

        private bool Slides => slideOffset != Vector2.zero;

        public void Show()
        {
            Init();
            _sequence?.Kill();
            gameObject.SetActive(true);
            IsVisible = true;
            _group.alpha = 0f;
            _group.interactable = true;
            _group.blocksRaycasts = true;

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Join(_group.DOFade(1f, fadeDuration).SetEase(Ease.OutQuad));
            if (content == null) return;

            if (Slides)
            {
                content.anchoredPosition = _contentPosition + slideOffset;
                _sequence.Join(content.DOAnchorPos(_contentPosition, showDuration).SetEase(showEase));
            }
            else
            {
                content.localScale = Vector3.one * startScale;
                _sequence.Join(content.DOScale(1f, showDuration).SetEase(showEase));
            }
        }

        public void Hide(bool instant = false)
        {
            Init();
            _sequence?.Kill();
            IsVisible = false;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            if (instant || !gameObject.activeInHierarchy)
            {
                _group.alpha = 0f;
                ResetContent();
                gameObject.SetActive(false);
                return;
            }

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Join(_group.DOFade(0f, hideDuration).SetEase(Ease.InQuad));
            if (content != null)
            {
                if (Slides) _sequence.Join(content.DOAnchorPos(_contentPosition + slideOffset, hideDuration).SetEase(hideEase));
                else _sequence.Join(content.DOScale(startScale, hideDuration).SetEase(hideEase));
            }
            _sequence.OnComplete(() =>
            {
                ResetContent();
                gameObject.SetActive(false);
            });
        }

        private void ResetContent()
        {
            if (content == null) return;
            content.anchoredPosition = _contentPosition;
            content.localScale = Vector3.one;
        }

        private void Init()
        {
            if (_initialized) return;
            _initialized = true;
            _group = GetComponent<CanvasGroup>();
            if (content != null) _contentPosition = content.anchoredPosition;
        }
    }
}
