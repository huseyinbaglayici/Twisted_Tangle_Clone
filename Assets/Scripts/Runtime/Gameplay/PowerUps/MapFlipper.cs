using System;
using System.Collections.Generic;
using DG.Tweening;
using Obi;
using Runtime.Entities;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // Flips the board 180 degrees around the screen's up axis (left <-> right) with a jump, while pins and slots
    // counter-rotate so they land upright. The ropes simulate in world space under the bootstrap solver, so the
    // same rigid motion is applied to their particles at every physics step: they stay attached and come out
    // mirrored (over/under swapped). The floor collider is off meanwhile, half the board dips below it mid-flip,
    // and gravity is off so slack ropes don't sag through the floor while it is.
    public class MapFlipper : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private Transform cameraRig;
        [SerializeField] private ObiCollider floor;

        [Header("Flip")]
        [Tooltip("Height of the rotation axis: the rope anchor height, so pins land back at board level.")]
        [SerializeField] private float pivotHeight = 0.2f;
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float duration = 1f;
        [SerializeField] private Ease ease = Ease.OutQuad;

        [Header("Camera")]
        [SerializeField] private float cameraPullBack = 3f;
        [SerializeField] private float cameraDuration = 0.5f;
        [SerializeField] private Ease cameraEase = Ease.OutQuad;

        [Header("After the flip")]
        [Tooltip("Particles end at least this high, in case a rope stacked high got mirrored under the floor.")]
        [SerializeField] private float minParticleHeight = 0.14f;
        [SerializeField] private float tenseRatio = 0.4f;
        [SerializeField] private float settleStep = 0.1f;

        private struct Item
        {
            public Transform Transform;
            public Vector3 Anchor;
            public Vector3 LocalAnchor;
            public Quaternion Rotation;
        }

        private readonly List<Item> _items = new();
        private readonly List<RopeEntity> _ropes = new();
        private Vector3 _center;
        private Vector3 _cameraHome;
        private float _progress;
        private Matrix4x4 _applied;
        private bool _tweenDone, _carryDone;
        private ObiSolver _solver;
        private Vector3 _gravity;
        private Tween _tween, _cameraTween;
        private Action _done;

        public bool IsFlipping { get; private set; }

        public void Flip(Action onDone)
        {
            Stop();
            _done = onDone;
            _center = level.Grid.Origin + Vector3.up * pivotHeight;
            CollectBoard();

            IsFlipping = true;
            _progress = 0f;
            _applied = Matrix4x4.identity;
            _tweenDone = _carryDone = false;
            floor.enabled = false;
            _solver = level.Solver;
            _gravity = _solver.gravity;
            _solver.gravity = Vector3.zero;
            _solver.OnSimulationStart += CarryRopes;

            _tween = DOTween.To(() => _progress, v =>
                {
                    _progress = v;
                    MoveBoard();
                }, 1f, duration).SetEase(Ease.Linear).SetTarget(this)
                .OnComplete(() => _tweenDone = true);

            _cameraHome = cameraRig.position;
            _cameraTween = cameraRig.DOMove(_cameraHome - cameraRig.forward * cameraPullBack, cameraDuration)
                .SetEase(cameraEase).SetLoops(2, LoopType.Yoyo).SetTarget(this);
        }

        // Level reset mid-flip: put the camera and physics back, the board is rebuilt anyway.
        public void Stop()
        {
            if (!IsFlipping) return;
            _tween?.Kill();
            _cameraTween?.Kill();
            cameraRig.position = _cameraHome;
            Unhook();
            _items.Clear();
            _ropes.Clear();
            _done = null;
        }

        private void CollectBoard()
        {
            _items.Clear();
            _ropes.Clear();
            foreach (var pin in level.PinViews.Values)
            {
                var model = pin.Model;
                if (model == null || model.Removed) continue;
                pin.transform.DOKill();
                pin.transform.SetPositionAndRotation(level.Grid.CellToWorld(model.Cell), Quaternion.identity);
                level.SetPinPlaced(model.Id, false);
                Add(pin.transform);
            }
            foreach (var cell in level.Cells.Values) Add(cell.transform);
            foreach (var rope in level.RopeViews.Values)
                if (rope.Model != null && !rope.Model.Merged && rope.Obi.isLoaded)
                    _ropes.Add(rope);
        }

        // Each object turns around its own point at pivot height, so it ends exactly mirrored at its old height.
        private void Add(Transform t)
        {
            var pos = t.position;
            var anchor = new Vector3(pos.x, _center.y, pos.z);
            _items.Add(new Item
            {
                Transform = t,
                Anchor = anchor,
                LocalAnchor = Quaternion.Inverse(t.rotation) * (anchor - pos),
                Rotation = t.rotation
            });
        }

        private float Eased(float t) => DOVirtual.EasedValue(0f, 1f, t, ease);

        // Rotation about the board's up-screen axis plus the jump, as one rigid transform.
        private Matrix4x4 BoardFrame(float e, out Quaternion spin)
        {
            spin = Quaternion.AngleAxis(180f * e, Vector3.forward);
            // Up half eases out, down half eases in, like a single jump.
            float h = e < 0.5f ? 1f - (1f - 2f * e) * (1f - 2f * e) : 1f - (2f * e - 1f) * (2f * e - 1f);
            return Matrix4x4.Translate(_center + Vector3.up * (jumpHeight * h)) * Matrix4x4.Rotate(spin) *
                   Matrix4x4.Translate(-_center);
        }

        private void MoveBoard()
        {
            float e = Eased(_progress);
            var frame = BoardFrame(e, out var spin);
            // Slots and pins add a local (180, 180, 0) turn over the same time, which cancels the flip at the end.
            var upright = spin * Quaternion.Euler(180f * e, 180f * e, 0f);
            foreach (var item in _items)
            {
                if (item.Transform == null) continue;
                var rotation = upright * item.Rotation;
                var anchor = frame.MultiplyPoint3x4(item.Anchor);
                item.Transform.SetPositionAndRotation(anchor - rotation * item.LocalAnchor, rotation);
            }
        }

        // Physics step start: move every rope particle by the board motion since the last step.
        private void CarryRopes(ObiSolver solver, float timeToSimulate, float substepTime)
        {
            if (_carryDone) return;
            bool last = _tweenDone;
            var frame = BoardFrame(Eased(last ? 1f : _progress), out _);
            var delta = solver.transform.worldToLocalMatrix * frame * _applied.inverse * solver.transform.localToWorldMatrix;
            _applied = frame;

            foreach (var rope in _ropes)
            {
                if (rope == null || !rope.Obi.isLoaded) continue;
                var actor = rope.Obi;
                for (int i = 0; i < actor.activeParticleCount; i++)
                {
                    int s = actor.solverIndices[i];
                    var p = (Vector3)delta.MultiplyPoint3x4(solver.positions[s]);
                    if (last) p = ClampAboveFloor(solver, p);
                    solver.positions[s] = new Vector4(p.x, p.y, p.z, solver.positions[s].w);
                    solver.prevPositions[s] = Move(delta, solver.prevPositions[s]);
                    solver.startPositions[s] = Move(delta, solver.startPositions[s]);
                    solver.endPositions[s] = Move(delta, solver.endPositions[s]);
                    solver.renderablePositions[s] = Move(delta, solver.renderablePositions[s]);
                    var v = delta.MultiplyVector(solver.velocities[s]);
                    solver.velocities[s] = new Vector4(v.x, v.y, v.z, 0f);
                }
            }
            if (last) _carryDone = true;
        }

        private static Vector4 Move(Matrix4x4 m, Vector4 p)
        {
            var q = m.MultiplyPoint3x4(p);
            return new Vector4(q.x, q.y, q.z, p.w);
        }

        private Vector3 ClampAboveFloor(ObiSolver solver, Vector3 local)
        {
            var world = solver.transform.TransformPoint(local);
            float min = level.Grid.Origin.y + minParticleHeight;
            if (world.y >= min) return local;
            world.y = min;
            return solver.transform.InverseTransformPoint(world);
        }

        private void Update()
        {
            if (!IsFlipping || !_carryDone) return;
            Complete();
        }

        private void Complete()
        {
            Unhook();
            MirrorBoard();
            SettleTenseRopes();
            var done = _done;
            _done = null;
            _items.Clear();
            _ropes.Clear();
            done?.Invoke();
        }

        // Pins sit on mirrored cells now; slot views are identical, so each one just snaps back to its own cell.
        private void MirrorBoard()
        {
            int w = level.Grid.Width;
            foreach (var pin in level.PinViews.Values)
            {
                var model = pin.Model;
                if (model == null || model.Removed) continue;
                model.Cell = new Vector2Int(w - 1 - model.Cell.x, model.Cell.y);
                pin.transform.SetPositionAndRotation(level.Grid.CellToWorld(model.Cell), Quaternion.identity);
                level.SetPinPlaced(model.Id, true);
            }
            foreach (var cell in level.Cells.Values)
            {
                cell.transform.rotation = Quaternion.identity;
                cell.Setup(cell.Cell, level.Grid.CellToWorld(cell.Cell));
            }
        }

        // Ropes left over-stretched by the flip let other ropes pass for a moment, one after another, to untangle.
        private void SettleTenseRopes()
        {
            int i = 0;
            foreach (var rope in level.RopeViews.Values)
            {
                if (rope.Model == null || rope.Model.Merged || rope.Model.Merging || rope.TensionRatio <= tenseRatio)
                    continue;
                rope.IgnoreRopesFor(settleStep * i, settleStep);
                i++;
            }
        }

        private void Unhook()
        {
            if (_solver != null)
            {
                _solver.OnSimulationStart -= CarryRopes;
                _solver.gravity = _gravity;
            }
            _solver = null;
            floor.enabled = true;
            IsFlipping = false;
        }

        private void OnDestroy() => Stop();
    }
}
