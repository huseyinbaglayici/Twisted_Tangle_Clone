using DG.Tweening;
using Runtime.Boostrap.Pool;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Entities
{
    public class PinEntity : MonoBehaviour, IPoolable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer pinRenderer;
        [SerializeField] private GameObject lockVisual;
        [SerializeField] private Collider pickCollider;   // put on a "Pin" layer for raycasts

        private MaterialPropertyBlock _block;

        public BoardModel.Pin Model { get; private set; }

        private void Awake()
        {
            if (pinRenderer == null) pinRenderer = GetComponentInChildren<Renderer>();
            if (pickCollider == null) pickCollider = GetComponentInChildren<Collider>();
            _block = new MaterialPropertyBlock();
        }

        public void Setup(BoardModel.Pin model, Vector3 world, Color color)
        {
            Model = model;
            transform.position = world;
            transform.localScale = Vector3.one;
            SetColor(color);
            if (lockVisual != null) lockVisual.SetActive(model.Locked);
        }

        public void SetSelected(bool on) => transform.DOScale(on ? 1.15f : 1f, 0.1f);

        private void SetColor(Color color)
        {
            if (pinRenderer == null) return;
            pinRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            pinRenderer.SetPropertyBlock(_block);
        }

        public void Spawn()
        {
            if (pickCollider != null) pickCollider.enabled = true;
        }

        public void Despawn()
        {
            transform.DOKill();
            Model = null;
        }
    }
}
