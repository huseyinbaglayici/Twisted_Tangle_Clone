using System;
using System.Collections.Generic;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Physics-driven win loop (fixed step, paused from the moment a movable pin is pressed until it is let go):
    //  - a rope is free once Obi reports no rope contact for successCheckDelay,
    //  - a free rope merges when one of its pins holds only it and neither it nor its connected ropes touch or
    //    cross another rope (cross = particles at the same XZ point with different heights),
    //  - the level is won when nothing is colliding or waiting to merge for successCheckDelay.
    public class MapFinishChecker : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private RopeMergeAnimator mergeAnimator;
        [SerializeField] private float successCheckDelay = 0.15f;
        [SerializeField] private PinInputController input;
        [SerializeField] private bool mergeOnAir = true;
        [SerializeField] private float ropesIntersectRadius = 0.15f;
        [SerializeField] private float minYDiffToCheckIntersection = 0.1f;
        [SerializeField] private float outOfMovesDelay = 0.2f;
        [SerializeField] private bool debugLog;

        private class RopeData
        {
            public bool IsColliding = true;
            public float Timer;
        }

        private readonly Dictionary<BoardModel.Rope, RopeData> _data = new();
        private readonly List<Vector3> _pointsA = new(), _pointsB = new();
        private readonly List<BoardModel.Rope> _group = new();
        private float _successTimer, _outOfMovesTimer;
        private bool _running;

        public event Action Won;
        public event Action OutOfMoves;

        public bool Succeeded { get; private set; }

        private BoardModel Board => level.Board;

        public void Begin()
        {
            _data.Clear();
            collisions.Clear();
            _successTimer = 0f;
            _outOfMovesTimer = 0f;
            Succeeded = false;
            _running = true;
        }

        public void Stop() => _running = false;

        private void FixedUpdate()
        {
            // Paused (timers frozen) while a pin is held or the level is still being created.
            if (!_running || Succeeded || input.IsHoldingPin || !AllRopesReady()) return;
            float dt = Time.fixedDeltaTime;

            foreach (var rope in Board.Ropes)
            {
                if (rope.Merged || rope.Merging) continue;
                if (!_data.TryGetValue(rope, out var d)) _data[rope] = d = new RopeData();
                if (!d.IsColliding) continue;

                if (collisions.IsColliding(View(rope).Obi))
                {
                    d.Timer = 0f;
                }
                else if (mergeOnAir || ArePinsPlaced(rope))
                {
                    d.Timer += dt;
                    if (d.Timer >= successCheckDelay)
                    {
                        d.IsColliding = false;
                        if (debugLog) Debug.Log($"[MapFinishChecker] rope {rope.Id} free t={Time.time:F2}");
                    }
                }
            }

            foreach (var rope in Board.Ropes)
                if (!rope.Merged && !rope.Merging && _data.TryGetValue(rope, out var d) && !d.IsColliding && CanMerge(rope))
                    Merge(rope);

            bool anyColliding = false, anyFreeWaiting = false, anyMerging = false;
            foreach (var rope in Board.Ropes)
            {
                if (rope.Merging) anyMerging = true;
                if (rope.Merged || rope.Merging || !_data.TryGetValue(rope, out var d)) continue;
                if (d.IsColliding) anyColliding = true;
                else anyFreeWaiting = true;
            }

            CheckOutOfMoves(dt, anyMerging, anyFreeWaiting);

            if (anyColliding || anyFreeWaiting)
            {
                _successTimer = 0f;
                return;
            }

            _successTimer += dt;
            if (_successTimer >= successCheckDelay && !anyMerging)
            {
                Succeeded = true;
                _running = false;
                Won?.Invoke();
            }
        }

        private void CheckOutOfMoves(float dt, bool anyMerging, bool anyFreeWaiting)
        {
            if (Board.MoveLimit <= 0 || Board.MovesUsed < Board.MoveLimit || anyMerging || anyFreeWaiting)
            {
                _outOfMovesTimer = 0f;
                return;
            }

            _outOfMovesTimer += dt;
            if (_outOfMovesTimer >= outOfMovesDelay)
            {
                _running = false;
                OutOfMoves?.Invoke();
            }
        }

        private bool CanMerge(BoardModel.Rope rope)
        {
            if (!BoardModel.HasPinWithSingleRope(rope)) return false;
            if (!mergeOnAir && !ArePinsPlaced(rope)) return false;

            ConnectedGroup(rope);
            foreach (var member in _group)
            {
                if (IsCollidingOutsideGroup(member)) return false;
                if (IsIntersectingOutsideGroup(member)) return false;
            }
            return true;
        }

        private void Merge(BoardModel.Rope rope)
        {
            if (debugLog)
                Debug.Log($"[MapFinishChecker] merge rope {rope.Id} t={Time.time:F2} colliding={collisions.IsColliding(View(rope).Obi)}");
            _data.Remove(rope);
            Board.BeginMerge(rope);
            mergeAnimator.Play(rope);
        }

        // Ropes reachable through shared pins (including the rope itself).
        private void ConnectedGroup(BoardModel.Rope start)
        {
            _group.Clear();
            _group.Add(start);
            for (int i = 0; i < _group.Count; i++)
            {
                var r = _group[i];
                foreach (var pin in new[] { r.A, r.B })
                foreach (var other in pin.Ropes)
                    if (!other.Merged && !_group.Contains(other))
                        _group.Add(other);
            }
        }

        private bool IsCollidingOutsideGroup(BoardModel.Rope rope)
        {
            var actor = View(rope).Obi;
            foreach (var other in Board.Ropes)
            {
                if (other.Merged || other.Merging || _group.Contains(other)) continue;
                if (collisions.IsCollidingWith(actor, View(other).Obi)) return true;
            }
            return false;
        }

        private bool IsIntersectingOutsideGroup(BoardModel.Rope rope)
        {
            View(rope).GetParticlePositions(_pointsA);
            foreach (var other in Board.Ropes)
            {
                if (other.Merged || other.Merging || _group.Contains(other)) continue;
                View(other).GetParticlePositions(_pointsB);
                if (HasIntersectionInPoint(_pointsA, _pointsB)) return true;
            }
            return false;
        }

        // Same XZ spot (closer than ropesIntersectRadius) at clearly different heights => one rope lies on the other.
        private bool HasIntersectionInPoint(List<Vector3> a, List<Vector3> b)
        {
            float r2 = ropesIntersectRadius * ropesIntersectRadius;
            foreach (var p in a)
            foreach (var q in b)
            {
                if (Mathf.Abs(p.y - q.y) < minYDiffToCheckIntersection) continue;
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        private bool ArePinsPlaced(BoardModel.Rope rope) =>
            level.IsPinPlaced(rope.A.Id) && level.IsPinPlaced(rope.B.Id);

        private bool AllRopesReady()
        {
            foreach (var rope in Board.Ropes)
                if (!rope.Merged && level.RopeViews.TryGetValue(rope.Id, out var v) && !v.IsReady)
                    return false;
            return true;
        }

        private RopeEntity View(BoardModel.Rope rope) => level.RopeViews[rope.Id];
    }
}
