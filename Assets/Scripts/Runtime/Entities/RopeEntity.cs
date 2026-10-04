using DG.Tweening;
using Runtime.Boostrap.Pool;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Entities
{
    // Obi-free rope view: LineRenderer through BoardModel.Rope.Path (authored shape, remapped after moves).
    // Per-point heights + layer offset give the over/under look. Obi Rope replaces this later.
    [RequireComponent(typeof(LineRenderer))]
    public class RopeEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] private float width = 0.12f;
        [SerializeField] private float layerHeight = 0.06f;
        [SerializeField] private Color tensionColor = Color.red;

        private LineRenderer _line;
        private Color _color;
        private Vector3[] _points;

        public BoardModel.Rope Model { get; private set; }
        public float Shrink { get; set; }   // 0..1, used by merge animation

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.widthMultiplier = width;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 4;
        }

        public void Setup(BoardModel.Rope model, Color color)
        {
            Model = model;
            _color = color;
            Shrink = 0f;
            if (_points == null || _points.Length != model.Path.Length) _points = new Vector3[model.Path.Length];
            _line.positionCount = _points.Length;
            _line.sortingOrder = model.Layer;
            Refresh();
        }

        // Called from LevelBuilder.LateUpdate.
        public void Refresh()
        {
            if (Model == null) return;
            var path = Model.Path;
            int n = path.Length;
            var mid = (path[0] + path[n - 1]) * 0.5f;
            float lift = Model.Layer * layerHeight;

            for (int i = 0; i < n; i++)
            {
                var p = Vector2.Lerp(path[i], mid, Shrink);
                float h = Model.Heights[i] + lift;
                _points[i] = new Vector3(p.x, h, p.y);
            }
            _line.SetPositions(_points);

            float tension = Model.TensionPercentage;
            float red = Mathf.InverseLerp(RopeTensionRule.WarnPercentage, RopeTensionRule.RejectPercentage, tension);
            var c = Color.Lerp(_color, tensionColor, red);
            _line.startColor = _line.endColor = c;
        }

        public void Spawn() { }

        public void Despawn()
        {
            transform.DOKill();
            DOTween.Kill(this);
            Model = null;
        }
    }
}
