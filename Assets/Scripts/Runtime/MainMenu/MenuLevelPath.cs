using Reflex.Attributes;
using Runtime.Boostrap.Save;
using TMPro;
using UnityEngine;

namespace Runtime.MainMenu
{
    // Level path on the menu: the label authored as "1" shows the current level, "2" the next one, and so on.
    public class MenuLevelPath : MonoBehaviour
    {
        [Inject] private SaveService _save;

        private void Start()
        {
            int current = Mathf.Max(1, _save.Level);
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (int.TryParse(label.text, out int authored))
                    label.text = (current + authored - 1).ToString();
        }
    }
}
