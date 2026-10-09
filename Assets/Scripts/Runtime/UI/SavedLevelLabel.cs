using Reflex.Attributes;
using Runtime.Boostrap.Save;
using TMPro;
using UnityEngine;

namespace Runtime.UI
{
    // Writes the player's current level into a label, e.g. the menu play button: "Level 12".
    public class SavedLevelLabel : MonoBehaviour
    {
        [Inject] private SaveService _save;

        [SerializeField] private TMP_Text label;
        [SerializeField] private string format = "Level {0}";

        private void Start()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            int level = _save != null ? Mathf.Max(1, _save.Level) : 1;
            label.text = string.Format(format, level);
        }
    }
}
