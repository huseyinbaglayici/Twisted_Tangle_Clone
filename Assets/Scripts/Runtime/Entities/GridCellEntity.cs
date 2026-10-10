using DG.Tweening;
using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Entities
{
    // A hole on the board. Every cell of the map is a slot.
    // While a pin is dragged its home slot shows a pulsing dashed ring, and the slot it would drop into
    // gets a solid dark ring.
    public class GridCellEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] private float defaultScale = 0.38f;
        [SerializeField] private SpriteRenderer selectedOutline;
        [SerializeField] private SpriteRenderer originOutline;
        [SerializeField] private float originPulseScale = 1.1f;
        [SerializeField] private float originPulseDuration = 0.4f;

        private Vector3 _originScale;

        public Vector2Int Cell { get; private set; }

        private void Awake()
        {
            if (originOutline != null) _originScale = originOutline.transform.localScale;
        }

        public void Setup(Vector2Int cell, Vector3 world)
        {
            Cell = cell;
            transform.position = world;
            transform.localScale = Vector3.one * defaultScale;
            SetHighlight(false);
            SetOrigin(false);
        }

        // Drop candidate while dragging.
        public void SetHighlight(bool on)
        {
            if (selectedOutline != null) selectedOutline.enabled = on;
        }

        // The dragged pin's own slot.
        public void SetOrigin(bool on)
        {
            if (originOutline == null) return;
            var t = originOutline.transform;
            t.DOKill();
            t.localScale = _originScale;
            originOutline.enabled = on;
            if (on)
                t.DOScale(_originScale * originPulseScale, originPulseDuration).SetEase(Ease.InOutQuad)
                    .SetLoops(-1, LoopType.Yoyo);
        }

        public void Spawn() { }

        public void Despawn()
        {
            transform.DOKill();
            SetOrigin(false);
            SetHighlight(false);
        }
    }
}
