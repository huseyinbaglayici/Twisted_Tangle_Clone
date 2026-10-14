using DG.Tweening;
using Runtime.Level;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class DifficultyBanner : MonoBehaviour
    {
        [SerializeField] private Image dim;
        [SerializeField] private Image ribbon;
        [SerializeField] private RectTransform title;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private Image icon;
        [SerializeField] private Sprite hardIcon;
        [SerializeField] private Sprite veryHardIcon;

        [SerializeField] private float holdDuration = 1.8f;
        [SerializeField] private float hideDuration = 0.5f;
        [SerializeField] private Vector2 titleStartOffset = new(0f, -320f);

        private CanvasGroup _group;
        private Vector2 _titlePosition;
        private float _dimAlpha;
        private Sequence _sequence;

        public void Play(LevelDifficulty difficulty)
        {
            Init();
            _sequence?.Kill();
            if (!difficulty.IsHard())
            {
                gameObject.SetActive(false);
                return;
            }

            ribbon.color = DifficultyStyle.ColorOf(difficulty, ribbon.color);
            icon.sprite = difficulty == LevelDifficulty.VeryHard ? veryHardIcon : hardIcon;
            titleLabel.text = DifficultyStyle.NameOf(difficulty) + "\nLEVEL";

            gameObject.SetActive(true);
            _group.alpha = 1f;
            var iconRect = icon.rectTransform;

            _sequence = DOTween.Sequence().SetLink(gameObject)
                .Append(dim.DOFade(_dimAlpha, holdDuration).From(0f).SetEase(Ease.OutExpo))
                .Join(iconRect.DOScale(1f, holdDuration).From(0f).SetEase(Ease.OutExpo))
                .Join(title.DOAnchorPos(_titlePosition, holdDuration).From(_titlePosition + titleStartOffset)
                    .SetEase(Ease.OutExpo))
                .Append(_group.DOFade(0f, hideDuration).SetEase(Ease.InQuart))
                .Join(iconRect.DOScale(0f, hideDuration).SetEase(Ease.InQuart))
                .Join(title.DOAnchorPos(_titlePosition + titleStartOffset, hideDuration).SetEase(Ease.InQuart))
                .OnComplete(() => gameObject.SetActive(false));
        }

        private void Init()
        {
            if (_group != null) return;
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _titlePosition = title.anchoredPosition;
            _dimAlpha = dim.color.a;
        }
    }
}
