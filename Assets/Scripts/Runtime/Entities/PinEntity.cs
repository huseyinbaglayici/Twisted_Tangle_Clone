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
        [SerializeField] private Transform ropeConnectPoint; // rope end attachment target (0.2 above the board)

        [Header("Locked (nailed) pin")]
        [SerializeField] private GameObject lockVisual;       // nail head, shown only on locked pins
        [SerializeField] private Color lockedBodyColor = new(0.415f, 0.415f, 0.415f);

        private MaterialPropertyBlock _block;

        public BoardModel.Pin Model { get; private set; }
        public Transform RopeConnectPoint => ropeConnectPoint != null ? ropeConnectPoint : transform;

        private void Awake()
        {
            if (pinRenderer == null) pinRenderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        // A locked pin loses its rope color: plain metal body plus the nail head on top.
        public void Setup(BoardModel.Pin model, Vector3 world, Color color)
        {
            Model = model;
            transform.position = world;
            transform.localScale = Vector3.one;
            SetColor(model.Locked ? lockedBodyColor : color);
            if (lockVisual != null) lockVisual.SetActive(model.Locked);
        }

        private void SetColor(Color color)
        {
            if (pinRenderer == null) return;
            pinRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            pinRenderer.SetPropertyBlock(_block);
        }

        public void Spawn() { }

        public void Despawn()
        {
            transform.DOKill();
            Model = null;
        }
    }
}
