using System;
using System.Collections.Generic;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Runs on the physics step and pauses while a movable pin is held:
    //  - a rope is free once it has had no rope contact for settleTime,
    //  - a free rope merges when one of its pins holds only it and neither it nor its connected ropes touch or
    //    cross another rope (cross = particles at the same XZ point with clearly different heights),
    //  - the level is won when nothing is colliding or waiting to merge for settleTime.
    public class WinChecker : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private RopeMergeAnimator mergeAnimator;
        [SerializeField] private PinInputController input;
        [SerializeField] private float settleTime = 0.15f;
        [SerializeField] private float initialWait = 0.75f;
        [SerializeField] private bool mergeWhileHeld = true;
        [SerializeField] private float crossRadius = 0.15f;
        [SerializeField] private float crossMinHeightGap = 0.1f;

        private class RopeState
        {
            public bool IsColliding = true;
            public float Timer;
        }

        private readonly Dictionary<BoardModel.Rope, RopeState> _states = new();
        private readonly List<Vector3> _pointsA = new(), _pointsB = new();
        private readonly List<BoardModel.Rope> _group = new();
        private float _winTimer;
        private float _startTime;
        private bool _running;

        public event Action Won;

        public bool Succeeded { get; private set; }

        // A power-up is reshaping the board: no natural merge or win until it finishes.
        public bool MergesBlocked { get; set; }

        private BoardModel Board => level.Board;

        public void Begin()
        {
            _states.Clear();
            collisions.Clear();
            _winTimer = 0f;
            Succeeded = false;
            _running = true;
            _startTime = Time.time + initialWait;
        }

        public void Stop() => _running = false;

        private void FixedUpdate()
        {
            if (!_running || MergesBlocked || Succeeded || Time.time < _startTime || input.IsHoldingPin || !AllRopesReady()) return;
            float dt = Time.fixedDeltaTime;

            foreach (var rope in Board.Ropes)
            {
                if (rope.Merged || rope.Merging) continue;
                if (!_states.TryGetValue(rope, out var s)) _states[rope] = s = new RopeState();
                if (!s.IsColliding) continue;

                if (collisions.IsColliding(View(rope).Obi))
                {
                    s.Timer = 0f;
                }
                else if (mergeWhileHeld || PinsSettled(rope))
                {
                    s.Timer += dt;
                    if (s.Timer >= settleTime) s.IsColliding = false;
                }
            }

            foreach (var rope in Board.Ropes)
                if (!rope.Merged && !rope.Merging && _states.TryGetValue(rope, out var s) && !s.IsColliding && IsReadyToMerge(rope))
                    Merge(rope);

            bool anyColliding = false, anyFreeWaiting = false, anyMerging = false;
            foreach (var rope in Board.Ropes)
            {
                if (rope.Merging) anyMerging = true;
                if (rope.Merged || rope.Merging || !_states.TryGetValue(rope, out var s)) continue;
                if (s.IsColliding) anyColliding = true;
                else anyFreeWaiting = true;
            }

            if (anyColliding || anyFreeWaiting)
            {
                _winTimer = 0f;
                return;
            }

            _winTimer += dt;
            if (_winTimer >= settleTime && !anyMerging)
            {
                Succeeded = true;
                _running = false;
                Won?.Invoke();
            }
        }

        private bool IsReadyToMerge(BoardModel.Rope rope)
        {
            if (!BoardModel.HasFreeEnd(rope)) return false;
            if (!mergeWhileHeld && !PinsSettled(rope)) return false;

            CollectGroup(rope);
            foreach (var member in _group)
            {
                if (TouchesOutsideGroup(member)) return false;
                if (CrossesOutsideGroup(member)) return false;
            }
            return true;
        }

        private void Merge(BoardModel.Rope rope)
        {
            _states.Remove(rope);
            Board.BeginMerge(rope);
            mergeAnimator.Play(rope);
        }

        // Cut by a power-up: merges regardless of contacts. With no free end no pin can travel, so the rope just pops.
        public void ForceMerge(BoardModel.Rope rope)
        {
            if (rope.Merged || rope.Merging) return;
            _states.Remove(rope);
            Board.BeginMerge(rope);
            if (BoardModel.HasFreeEnd(rope)) mergeAnimator.Play(rope, reelIn: false);
            else mergeAnimator.Pop(rope);
        }

        private void CollectGroup(BoardModel.Rope start)
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

        private bool TouchesOutsideGroup(BoardModel.Rope rope)
        {
            var actor = View(rope).Obi;
            foreach (var other in Board.Ropes)
            {
                if (other.Merged || other.Merging || _group.Contains(other)) continue;
                if (collisions.IsCollidingWith(actor, View(other).Obi)) return true;
            }
            return false;
        }

        private bool CrossesOutsideGroup(BoardModel.Rope rope)
        {
            View(rope).CopyParticlePositions(_pointsA);
            foreach (var other in Board.Ropes)
            {
                if (other.Merged || other.Merging || _group.Contains(other)) continue;
                View(other).CopyParticlePositions(_pointsB);
                if (HasCrossing(_pointsA, _pointsB)) return true;
            }
            return false;
        }

        private bool HasCrossing(List<Vector3> a, List<Vector3> b)
        {
            float r2 = crossRadius * crossRadius;
            foreach (var p in a)
            foreach (var q in b)
            {
                if (Mathf.Abs(p.y - q.y) < crossMinHeightGap) continue;
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        private bool PinsSettled(BoardModel.Rope rope) =>
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
