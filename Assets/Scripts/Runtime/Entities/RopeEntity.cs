using System.Collections.Generic;
using DG.Tweening;
using Obi;
using Runtime.Boostrap.Pool;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Entities
{
    // Obi rope between two pins. 25 active particles, static attachments to each pin's rope connect point.
    // The authored path places every particle 1:1 when the level starts; physics takes over from there.
    [RequireComponent(typeof(ObiRope))]
    public class RopeEntity : MonoBehaviour, IPoolable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private ObiRope rope;
        [SerializeField] private ObiRopeExtrudedRenderer ropeRenderer;
        [SerializeField] private ObiRopeCursor cursor;
        [SerializeField] private ObiParticleAttachment startAttachment;
        [SerializeField] private ObiParticleAttachment endAttachment;

        [Header("Connection")]
        [SerializeField] private float pinEnterParticlesOffsetPercent = 0.15f;
        [SerializeField] private int normalCollisionFilterCategory = 1;
        [SerializeField] private int sharedCollisionFilterCategory = 15;

        [Header("Tension")]
        [SerializeField] private float minTension = 0f;
        [SerializeField] private float maxTension = 1.25f;
        [SerializeField] private float tensionColorMinPercentage = 0.45f;
        [SerializeField] private Color tensionColor = Color.red;

        private readonly List<Vector3> _pendingPositions = new();
        private Material _material;
        private Transform _startPoint, _endPoint;
        private Color _color;
        private bool _startShared, _endShared;

        public BoardModel.Rope Model { get; private set; }
        public ObiRope Obi => rope;
        public Color Color => _color;
        public bool IsReady => rope.isLoaded && _pendingPositions.Count == 0;

        // tension = current length / rest length - 1 (0 = slack, 1 = twice as long)
        public float Tension => rope.isLoaded && rope.restLength > 0.0001f ? rope.CalculateLength() / rope.restLength - 1f : 0f;
        public float TensionPercentage => Mathf.Clamp01((Tension - minTension) / (maxTension - minTension));

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
            startAttachment.target = null;
            endAttachment.target = null;

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

            SetEndFilter(0f, pinEnterParticlesOffsetPercent, _startShared);
            SetEndFilter(1f - pinEnterParticlesOffsetPercent, 1f, _endShared);
            _pendingPositions.Clear();

            startAttachment.target = _startPoint;
            endAttachment.target = _endPoint;
        }

        // Ends of ropes sharing a pin use their own category so they don't push each other apart at the pin.
        private void SetEndFilter(float fromPercent, float toPercent, bool shared)
        {
            int filter = shared
                ? (~(1 << sharedCollisionFilterCategory) << 16) | (1 << sharedCollisionFilterCategory)
                : ObiUtils.MakeFilter(ObiUtils.CollideWithEverything, normalCollisionFilterCategory);
            int last = rope.activeParticleCount - 1;
            int from = Mathf.RoundToInt(fromPercent * last), to = Mathf.RoundToInt(toPercent * last);
            for (int i = from; i <= to; i++)
                rope.solver.filters[rope.solverIndices[i]] = filter;
        }

        // Lengthens (+) / shortens (-) the rope; changes accumulate until the next solver step (merge animation).
        public void ChangeLength(float delta)
        {
            if (rope.isLoaded) cursor.ChangeLength(delta);
        }

        public float RestLength => rope.isLoaded ? rope.restLength : 0f;

        // Particle positions in world space (active particles only), for the intersection test.
        public void GetParticlePositions(List<Vector3> result)
        {
            result.Clear();
            if (!rope.isLoaded) return;
            var solver = rope.solver;
            for (int i = 0; i < rope.activeParticleCount; i++)
                result.Add(solver.transform.TransformPoint(solver.positions[rope.solverIndices[i]]));
        }

        private void LateUpdate()
        {
            if (Model == null || !rope.isLoaded) return;
            float red = Mathf.InverseLerp(tensionColorMinPercentage, 1f, TensionPercentage);
            SetColor(Color.Lerp(_color, tensionColor, red));
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
            startAttachment.target = null;
            endAttachment.target = null;
            _startPoint = _endPoint = null;
            _pendingPositions.Clear();
            Model = null;
        }
    }
}
