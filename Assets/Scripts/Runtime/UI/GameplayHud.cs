using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // In-game header (level, moves left, retry) and footer (undo, settings). Pure view: raises button events, shows data.
    public class GameplayHud : MonoBehaviour
    {
        [Header("Level")]
        [SerializeField] private TMP_Text levelNumberLabel;

        [Header("Moves")]
        [SerializeField] private GameObject movesRoot;
        [SerializeField] private TMP_Text movesLabel;
        [SerializeField] private RectTransform movesPunchTarget;
        [SerializeField] private Color movesColor = Color.white;
        [SerializeField] private Color movesWarningColor = new(1f, 0.38f, 0.32f);
        [SerializeField] private int movesWarningAt = 2;

        [Header("Buttons")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button settingsButton;

        [Header("Intro")]
        [SerializeField] private UiPanelAnimator header;
        [SerializeField] private UiPanelAnimator footer;

        private int _movesLeft = -1;

        public event Action RetryClicked;
        public event Action UndoClicked;
        public event Action SettingsClicked;

        private void Awake()
        {
            retryButton.onClick.AddListener(() => RetryClicked?.Invoke());
            undoButton.onClick.AddListener(() => UndoClicked?.Invoke());
            settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
        }

        public void Show()
        {
            if (header != null) header.Show();
            if (footer != null) footer.Show();
        }

        public void SetLevel(int level) => levelNumberLabel.text = level.ToString();

        // left < 0 = the level has no move limit.
        public void SetMoves(int left)
        {
            if (left < 0)
            {
                movesRoot.SetActive(false);
                _movesLeft = -1;
                return;
            }

            movesRoot.SetActive(true);
            if (left == _movesLeft) return;
            bool changed = _movesLeft >= 0;
            _movesLeft = left;
            movesLabel.text = left.ToString();
            movesLabel.color = left <= movesWarningAt ? movesWarningColor : movesColor;

            if (!changed || movesPunchTarget == null) return;
            movesPunchTarget.DOKill(true);
            movesPunchTarget.DOPunchScale(Vector3.one * 0.25f, 0.3f, 6, 0.5f).SetUpdate(true).SetLink(gameObject);
        }

        public void SetUndoInteractable(bool interactable) => undoButton.interactable = interactable;
    }
}
