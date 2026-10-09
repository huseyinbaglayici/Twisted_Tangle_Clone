using UnityEngine;

namespace Runtime.UI
{
    // Fits this RectTransform to Screen.safeArea (notches, home indicator). Put it on a full-stretch child of the canvas.
    [RequireComponent(typeof(RectTransform))]
    public class SafeArea : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _applied;
        private Vector2Int _screen;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != _applied || Screen.width != _screen.x || Screen.height != _screen.y) Apply();
        }

        private void Apply()
        {
            var area = Screen.safeArea;
            _applied = area;
            _screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _rect.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _rect.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
