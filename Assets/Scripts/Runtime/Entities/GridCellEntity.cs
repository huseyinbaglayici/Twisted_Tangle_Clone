using DG.Tweening;
using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Entities
{
    // A hole on the board. Every cell of the map is a slot.
    public class GridCellEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] private SpriteRenderer slotRenderer;
        [SerializeField] private Color highlightColor = new(1f, 1f, 1f, 0.35f);
        [SerializeField] private float defaultScale = 0.38f;
        [SerializeField] private float highlightScale = 0.48f;

        private Color _normalColor;

        public Vector2Int Cell { get; private set; }

        private void Awake()
        {
            if (slotRenderer == null)
                foreach (var r in GetComponentsInChildren<SpriteRenderer>())
                    if (r.enabled) { slotRenderer = r; break; }
            if (slotRenderer != null) _normalColor = slotRenderer.color;
        }

        public void Setup(Vector2Int cell, Vector3 world)
        {
            Cell = cell;
            transform.position = world;
            SetHighlight(false, instant: true);
        }

        public void SetHighlight(bool on, bool instant = false)
        {
            float s = on ? highlightScale : defaultScale;
            if (instant) transform.localScale = Vector3.one * s;
            else transform.DOScale(s, 0.12f);
            if (slotRenderer != null) slotRenderer.color = on ? highlightColor : _normalColor;
        }

        public void Spawn() { }

        public void Despawn() => transform.DOKill();
    }
}
