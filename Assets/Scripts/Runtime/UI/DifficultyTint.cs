using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    // Puts one UI graphic into the look of the level's difficulty; a null theme restores the authored look.
    // Pattern, Icon and Title pieces only exist on themed levels.
    public class DifficultyTint : MonoBehaviour
    {
        [SerializeField] private Graphic target;
        [SerializeField] private DifficultySlot slot;

        private Color _authoredColor;
        private bool _captured;

        public void Apply(DifficultyStyle.Theme theme)
        {
            if (!_captured)
            {
                _captured = true;
                _authoredColor = target.color;
            }

            switch (slot)
            {
                case DifficultySlot.Pattern:
                    if (theme != null) ((Image)target).sprite = theme.pattern;
                    gameObject.SetActive(theme != null);
                    break;
                case DifficultySlot.Icon:
                    if (theme != null) ((Image)target).sprite = theme.icon;
                    gameObject.SetActive(theme != null);
                    break;
                case DifficultySlot.Title:
                    if (theme != null) ((TMP_Text)target).text = theme.LevelTitle;
                    gameObject.SetActive(theme != null);
                    break;
                default:
                    target.color = theme != null ? theme.ColorOf(slot) : _authoredColor;
                    break;
            }
        }
    }
}
