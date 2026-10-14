using Runtime.Level;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    public class DifficultyBadge : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Sprite hardIcon;
        [SerializeField] private Sprite veryHardIcon;

        public void Set(LevelDifficulty difficulty)
        {
            bool show = difficulty.IsHard();
            gameObject.SetActive(show);
            if (!show) return;

            background.color = DifficultyStyle.ColorOf(difficulty, background.color);
            icon.sprite = difficulty == LevelDifficulty.VeryHard ? veryHardIcon : hardIcon;
            label.text = DifficultyStyle.NameOf(difficulty);
        }
    }
}
