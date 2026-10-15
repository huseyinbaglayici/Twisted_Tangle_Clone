using DG.Tweening;
using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Entities
{
    public class GridCellEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] private float baseScale = 0.38f;
        [SerializeField] private SpriteRenderer dropRing;
        [SerializeField] private SpriteRenderer originRing;
        [SerializeField] private GameObject blockedVisual;
        [SerializeField] private GameObject funnel;
        [SerializeField] private float originPulseScale = 1.1f;
        [SerializeField] private float originPulseDuration = 0.4f;

        private Vector3 _originScale;

        public Vector2Int Cell { get; private set; }

        private void Awake()
        {
            if (originRing != null) _originScale = originRing.transform.localScale;
        }

        public void Setup(Vector2Int cell, Vector3 world, bool blocked, bool hasFunnel)
        {
            Cell = cell;
            if (blockedVisual != null) blockedVisual.SetActive(blocked);
            if (funnel != null) funnel.SetActive(hasFunnel);
            transform.position = world;
            transform.localScale = Vector3.one * baseScale;
            SetHighlight(false);
            SetOrigin(false);
        }

        public void SetHighlight(bool on)
        {
            if (dropRing != null) dropRing.enabled = on;
        }

        public void SetOrigin(bool on)
        {
            if (originRing == null) return;
            var t = originRing.transform;
            t.DOKill();
            t.localScale = _originScale;
            originRing.enabled = on;
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
