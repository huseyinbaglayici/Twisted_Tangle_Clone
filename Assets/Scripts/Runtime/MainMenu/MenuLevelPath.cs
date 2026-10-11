using DG.Tweening;
using Reflex.Attributes;
using Runtime.Boostrap.Save;
using TMPro;
using UnityEngine;

namespace Runtime.MainMenu
{
    // Level path on the menu: the label authored as "1" shows the current level, "2" the next one, and so on.
    // The current level's ring breathes so the eye lands on it.
    public class MenuLevelPath : MonoBehaviour
    {
        [Inject] private SaveService _save;

        [SerializeField] private RectTransform focusRing;
        [SerializeField] private float focusScale = 0.15f;
        [SerializeField] private float focusDuration = 1f;

        private void Start()
        {
            int current = Mathf.Max(1, _save.Level);
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (int.TryParse(label.text, out int authored))
                    label.text = (current + authored - 1).ToString();

            if (focusRing == null) return;
            focusRing.DOScale(1f + focusScale, focusDuration).SetEase(Ease.InOutQuad)
                .SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
        }
    }
}
