using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class LevelCompletePanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup background;
        [SerializeField] private RectTransform title;
        [SerializeField] private RectTransform rays;
        [SerializeField] private RectTransform emoji;
        [SerializeField] private RectTransform continueRoot;
        [SerializeField] private Button continueButton;

        [Header("Intro")]
        [SerializeField] private float duration = 0.5f;
        [SerializeField] private Ease ease = Ease.OutExpo;
        [SerializeField] private float emojiDelay = 0.1f;
        [SerializeField] private Ease emojiEase = Ease.OutBack;
        [SerializeField] private float slideDistance = 1200f;
        [SerializeField] private float raysTurnTime = 8f;
        [SerializeField] private float hideDuration = 0.2f;

        private CanvasGroup _group;
        private Vector2 _continuePosition;
        private Sequence _sequence;
        private bool _initialized;

        public event Action ContinueClicked;

        public bool IsVisible { get; private set; }

        public void Show()
        {
            Init();
            Kill();
            IsVisible = true;
            gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            continueButton.interactable = true;

            background.alpha = 0f;
            title.localScale = Vector3.zero;
            emoji.localScale = Vector3.zero;
            continueRoot.anchoredPosition = _continuePosition + Vector2.down * slideDistance;

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
                .Join(background.DOFade(1f, duration).SetEase(ease))
                .Join(title.DOScale(1f, duration).SetEase(ease))
                .Join(continueRoot.DOAnchorPos(_continuePosition, duration).SetEase(ease))
                .Insert(emojiDelay, emoji.DOScale(1f, duration).SetEase(emojiEase));

            if (rays != null)
                rays.DOLocalRotate(new Vector3(0f, 0f, -360f), raysTurnTime, RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true).SetLink(gameObject);
        }

        public void Hide(bool instant = false)
        {
            Init();
            Kill();
            IsVisible = false;
            _group.blocksRaycasts = false;
            if (instant || !gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                return;
            }

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
                .Append(_group.DOFade(0f, hideDuration))
                .OnComplete(() => gameObject.SetActive(false));
        }

        private void Kill()
        {
            _sequence?.Kill();
            if (rays != null) rays.DOKill();
        }

        private void Init()
        {
            if (_initialized) return;
            _initialized = true;
            _group = GetComponent<CanvasGroup>();
            _continuePosition = continueRoot.anchoredPosition;
            continueButton.onClick.AddListener(() =>
            {
                continueButton.interactable = false;
                ContinueClicked?.Invoke();
            });
        }
    }
}
