using DG.Tweening;
using Reflex.Attributes;
using Runtime.Boostrap.Save;
using Runtime.Level;
using Runtime.UI;
using TMPro;
using UnityEngine;

namespace Runtime.MainMenu
{
    // The label authored as "1" shows the current level, "2" the next one, and so on.
    // A DifficultyBadge next to a label shows that level's difficulty.
    public class MenuLevelPath : MonoBehaviour
    {
        [Inject] private SaveService _save;

        [SerializeField] private RectTransform focusRing;
        [SerializeField] private float focusScale = 0.15f;
        [SerializeField] private float focusDuration = 1f;

        private void Start()
        {
            int current = Mathf.Max(1, _save.Level);
            var levels = new LevelRepository();
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
            {
                if (!int.TryParse(label.text, out int authored)) continue;
                int level = current + authored - 1;
                label.text = level.ToString();

                var badge = label.transform.parent.GetComponentInChildren<DifficultyBadge>(true);
                if (badge != null) badge.Set(levels.DifficultyOf(level));
            }

            if (focusRing == null) return;
            focusRing.DOScale(1f + focusScale, focusDuration).SetEase(Ease.InOutQuad)
                .SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
        }
    }
}
