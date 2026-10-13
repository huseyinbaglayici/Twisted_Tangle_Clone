using System.Collections.Generic;
using DG.Tweening;
using Obi;
using Runtime.Boostrap.Pool;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Entities
{
    [RequireComponent(typeof(ObiRope))]
    public class RopeEntity : MonoBehaviour, IPoolable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private ObiRope rope;
        [SerializeField] private ObiRopeExtrudedRenderer ropeRenderer;
        [SerializeField] private ObiRopeCursor cursor;
        [SerializeField] private ObiParticleAttachment attachmentA;
        [SerializeField] private ObiParticleAttachment attachmentB;

        [Header("Rope ends")]
        [SerializeField] private float endFilterPercent = 0.15f;
        [SerializeField] private int ropeCategory = 1;
        [SerializeField] private int sharedEndCategory = 15;

        [Header("Tension")]
        [SerializeField] private float tensionFloor = 0f;
        [SerializeField] private float tensionCeiling = 1.25f;
        [SerializeField] private float redTintFrom = 0.45f;
        [SerializeField] private Color tensionColor = Color.red;

        [Header("Blink (power-up target)")]
        [SerializeField] private Color blinkColor = Color.white;
        [SerializeField, Range(0f, 1f)] private float blinkStrength = 0.65f;
        [SerializeField] private float blinkDuration = 0.25f;

        private readonly List<Vector3> _pendingPositions = new();
        private readonly List<int> _savedFilters = new();
        private Material _material;
        private Transform _startPoint, _endPoint;
        private Color _color;
        private bool _startShared, _endShared;
        private Tween _blink;
        private float _blinkAmount;

        public BoardModel.Rope Model { get; private set; }
        public ObiRope Obi => rope;
        public Color Color => _color;
        public bool IsReady => rope.isLoaded && _pendingPositions.Count == 0;
        public bool IsCut { get; private set; }

        // 0 = at rest length, 1 = stretched to twice its rest length.
        public float Tension => rope.isLoaded && rope.restLength > 0.0001f ? rope.CalculateLength() / rope.restLength - 1f : 0f;
        public float TensionRatio => Mathf.Clamp01((Tension - tensionFloor) / (tensionCeiling - tensionFloor));

        private void Awake()
        {
            _material = new Material(ropeRenderer.material);
            ropeRenderer.material = _material;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Setup(BoardModel.Rope model, Color color, Transform startPoint, Transform endPoint,
            IReadOnlyList<Vector3> particlePositions, bool startShared, bool endShared)
        {
            Model = model;
            _color = color;
            _startShared = startShared;
            _endShared = endShared;
            SetColor(color);

            // Attachments keep the particle-to-target offset they are bound with, so they are bound only after the
            // particles sit on the pins (ApplyPendingState).
            _startPoint = startPoint;
            _endPoint = endPoint;
            attachmentA.target = null;
            attachmentB.target = null;

            _pendingPositions.Clear();
            _pendingPositions.AddRange(particlePositions);
            if (rope.isLoaded) ApplyPendingState();
        }

        private void OnBlueprintLoaded(ObiActor actor, ObiActorBlueprint blueprint) => ApplyPendingState();

        // Runs once the actor is inside the solver: authored particle positions, zero velocity, end filters.
        private void ApplyPendingState()
        {
            if (_pendingPositions.Count == 0) return;
            var solver = rope.solver;
            int count = Mathf.Min(rope.activeParticleCount, _pendingPositions.Count);
            for (int i = 0; i < count; i++)
            {
                int s = rope.solverIndices[i];
                Vector4 p = solver.transform.InverseTransformPoint(_pendingPositions[i]);
                solver.positions[s] = p;
                solver.prevPositions[s] = p;
                solver.renderablePositions[s] = p;
                solver.startPositions[s] = p;
                solver.endPositions[s] = p;
                solver.velocities[s] = Vector4.zero;
            }

            SetEndFilter(0f, endFilterPercent, _startShared);
            SetEndFilter(1f - endFilterPercent, 1f, _endShared);
            _pendingPositions.Clear();

            attachmentA.target = _startPoint;
            attachmentB.target = _endPoint;
        }

        // Ends of ropes sharing a pin use their own category so they don't push each other apart at the pin.
        private void SetEndFilter(float fromPercent, float toPercent, bool shared)
        {
            int filter = shared
                ? (~(1 << sharedEndCategory) << 16) | (1 << sharedEndCategory)
                : ObiUtils.MakeFilter(ObiUtils.CollideWithEverything, ropeCategory);
            int last = rope.activeParticleCount - 1;
            int from = Mathf.RoundToInt(fromPercent * last), to = Mathf.RoundToInt(toPercent * last);
            for (int i = from; i <= to; i++)
                rope.solver.filters[rope.solverIndices[i]] = filter;
        }

        public void ChangeLength(float delta)
        {
            if (rope.isLoaded) cursor.ChangeLength(delta);
        }

        public float RestLength => rope.isLoaded ? rope.restLength : 0f;

        public void CopyParticlePositions(List<Vector3> result)
        {
            result.Clear();
            if (!rope.isLoaded) return;
            var solver = rope.solver;
            for (int i = 0; i < rope.activeParticleCount; i++)
                result.Add(solver.transform.TransformPoint(solver.positions[rope.solverIndices[i]]));
        }

        public void SetBlink(bool on)
        {
            _blink?.Kill();
            _blink = null;
            _blinkAmount = 0f;
            if (on)
                _blink = DOTween.To(() => _blinkAmount, v => _blinkAmount = v, 1f, blinkDuration)
                    .SetEase(Ease.Linear).SetLoops(-1, LoopType.Yoyo).SetTarget(this);
        }

        // Tears the rope at a segment (particle index -> index + 1) and lets both pieces shrink towards their pins.
        // The first segment is never torn: its first particle sits on the pin.
        public bool Cut(int segment, float shrink)
        {
            if (!rope.isLoaded || IsCut || rope.elements.Count < 2) return false;
            int i = Mathf.Clamp(segment, 1, rope.elements.Count - 1);
            if (!rope.Tear(rope.elements[i])) return false;
            rope.RebuildConstraintsFromElements();
            rope.stretchingScale = shrink;
            IsCut = true;
            return true;
        }

        // For a moment collide only with category 0 (floor and pins), so other ropes can slip through it.
        public void IgnoreRopesFor(float delay, float seconds)
        {
            DOTween.Sequence().SetTarget(this)
                .AppendInterval(delay)
                .AppendCallback(() => SetRopeCollisions(false))
                .AppendInterval(seconds)
                .AppendCallback(() => SetRopeCollisions(true));
        }

        private void SetRopeCollisions(bool on)
        {
            if (!rope.isLoaded) return;
            var filters = rope.solver.filters;
            if (!on) _savedFilters.Clear();
            for (int i = 0; i < rope.activeParticleCount; i++)
            {
                int s = rope.solverIndices[i];
                if (on)
                {
                    if (i < _savedFilters.Count) filters[s] = _savedFilters[i];
                }
                else
                {
                    _savedFilters.Add(filters[s]);
                    filters[s] = (1 << 16) | (filters[s] & 0xFFFF);
                }
            }
        }

        private void LateUpdate()
        {
            if (Model == null || !rope.isLoaded) return;
            float red = Mathf.InverseLerp(redTintFrom, 1f, TensionRatio);
            var color = Color.Lerp(_color, tensionColor, red);
            SetColor(Color.Lerp(color, blinkColor, _blinkAmount * blinkStrength));
        }

        private void SetColor(Color c)
        {
            if (_material.HasProperty(BaseColorId)) _material.SetColor(BaseColorId, c);
            if (_material.HasProperty(ColorId)) _material.SetColor(ColorId, c);
        }

        public void Spawn() => rope.OnBlueprintLoaded += OnBlueprintLoaded;

        public void Despawn()
        {
            rope.OnBlueprintLoaded -= OnBlueprintLoaded;
            transform.DOKill();
            DOTween.Kill(this);
            _blink = null;
            _blinkAmount = 0f;
            IsCut = false;
            _savedFilters.Clear();
            rope.stretchingScale = 1f;
            attachmentA.target = null;
            attachmentB.target = null;
            _startPoint = _endPoint = null;
            _pendingPositions.Clear();
            Model = null;
        }
    }
}
