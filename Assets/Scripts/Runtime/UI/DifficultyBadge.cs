using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.UI
{
    public class DifficultyBadge : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;

        public void Set(DifficultyStyle.Theme theme)
        {
            gameObject.SetActive(theme != null);
            if (theme == null) return;

            background.sprite = theme.label;
            label.text = theme.name;
        }
    }
}
