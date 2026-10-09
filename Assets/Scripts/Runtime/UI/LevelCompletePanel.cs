using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // "Level Complete!" screen: ribbon title, stars popping in one by one, continue button.
    [RequireComponent(typeof(UiPanelAnimator))]
    public class LevelCompletePanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private RectTransform[] stars;
        [SerializeField] private float starDelay = 0.3f;
        [SerializeField] private float starStagger = 0.12f;
        [SerializeField] private float starDuration = 0.35f;

        private UiPanelAnimator _animator;
        private bool _listening;

        public event Action ContinueClicked;

        public bool IsVisible => Animator.IsVisible;

        private UiPanelAnimator Animator => _animator != null ? _animator : _animator = GetComponent<UiPanelAnimator>();

        public void Show(int completedLevel)
        {
            Listen();
            levelLabel.text = $"Level {completedLevel}";
            continueButton.interactable = true;
            Animator.Show();

            for (int i = 0; i < stars.Length; i++)
            {
                var star = stars[i];
                star.DOKill();
                star.localScale = Vector3.zero;
                star.DOScale(1f, starDuration).SetDelay(starDelay + i * starStagger).SetEase(Ease.OutBack)
                    .SetUpdate(true).SetLink(gameObject);
            }
        }

        public void Hide(bool instant = false) => Animator.Hide(instant);

        private void Listen()
        {
            if (_listening) return;
            _listening = true;
            continueButton.onClick.AddListener(() =>
            {
                continueButton.interactable = false;
                ContinueClicked?.Invoke();
            });
        }
    }
}
