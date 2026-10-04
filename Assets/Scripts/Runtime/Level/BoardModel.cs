using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Level
{
    // Pure-C# game state (no MonoBehaviour). Views (PinEntity/RopeEntity) mirror it.
    // Geometry uses each pin's *current plane position* (dragged pins included), so a rope can merge mid-drag.
    public class BoardModel
    {
        public class Pin
        {
            public int Id;
            public Vector2Int Cell;        // committed slot
            public Vector2 Position;       // current board-plane position (world XZ), follows drag
            public bool Locked;
            public bool InputEnabled = true;
            public bool Removed;
            public readonly List<Rope> Ropes = new();
            public int ActiveRopeCount { get { int n = 0; foreach (var r in Ropes) if (!r.Merged) n++; return n; } }
        }

        public class Rope
        {
            public int Id;
            public Pin A, B;
            public int ColorIndex, Layer;
            public bool Merging, Merged;
            public float FreeTimer;        // time spent touching nothing
            public bool IsFree;            // touched nothing for SuccessCheckDelay
            public int CrossingCount;

            // Shape (board plane). BasePath = authored shape; Path = current (remapped after pin moves).
            public Vector2[] BasePath;
            public float[] Heights;        // per point, for rendering (over/under look)
            public float BasePathLength;
            public Vector2[] Path;
            public bool Reshaped;          // any pin of this rope moved since load

            public void UpdatePath()
            {
                if (!Reshaped) { System.Array.Copy(BasePath, Path, Path.Length); return; }
                RopeShape.Remap(BasePath, BasePathLength, A.Position, B.Position, Path);
            }

            public float Length => Vector2.Distance(A.Position, B.Position);
            public float TensionPercentage => RopeTensionRule.Percentage(Length, RopeTensionRule.RestLength);
            public Pin Other(Pin p) => p == A ? B : A;
        }

        public readonly Dictionary<int, Pin> Pins = new();
        public readonly List<Rope> Ropes = new();
        public BoardGrid Grid { get; private set; }
        public int MoveLimit { get; private set; }
        public int MovesUsed { get; private set; }

        public event Action<Pin, Vector2Int, Vector2Int> PinMoved; // pin, from, to
        public event Action<Rope> RopeMergeStarted;
        public event Action Won;
        public event Action OutOfMoves;

        public bool IsWon { get; private set; }

        public void Load(StageJson stage, int moveLimit, BoardGrid grid)
        {
            Pins.Clear(); Ropes.Clear(); IsWon = false; MovesUsed = 0;
            Grid = grid; MoveLimit = moveLimit;
            foreach (var p in stage.pins)
                Pins[p.id] = new Pin { Id = p.id, Cell = p.Cell, Position = grid.CellToPlane(p.Cell), Locked = p.locked };
            foreach (var r in stage.ropes)
            {
                var rope = new Rope { Id = r.id, A = Pins[r.pinA], B = Pins[r.pinB], ColorIndex = r.colorIndex, Layer = r.layer };
                BuildShape(rope, r, grid);
                rope.A.Ropes.Add(rope); rope.B.Ropes.Add(rope);
                Ropes.Add(rope);
            }
            RecalculateCrossings();
        }

        private static void BuildShape(Rope rope, RopeJson r, BoardGrid grid)
        {
            if (r.path != null && r.path.Count >= 2)
            {
                int n = r.path.Count;
                rope.BasePath = new Vector2[n]; rope.Heights = new float[n];
                var origin = grid.CellToPlane(Vector2Int.zero);
                for (int i = 0; i < n; i++)
                {
                    rope.BasePath[i] = origin + new Vector2(r.path[i].x, r.path[i].y) * grid.Spacing; // cell space -> plane
                    rope.Heights[i] = r.path[i].h;
                }
                // snap ends exactly onto pins
                rope.BasePath[0] = rope.A.Position; rope.BasePath[n - 1] = rope.B.Position;
            }
            else
            {
                rope.BasePath = RopeShape.Straight(rope.A.Position, rope.B.Position);
                rope.Heights = new float[rope.BasePath.Length];
                for (int i = 0; i < rope.Heights.Length; i++) rope.Heights[i] = 0.2f + 0.1f * Mathf.Sin(Mathf.PI * i / (rope.Heights.Length - 1f));
            }
            rope.BasePathLength = RopeGeometry.PathLength(rope.BasePath);
            rope.Path = (Vector2[])rope.BasePath.Clone();
        }

        public bool IsCellFree(Vector2Int cell)
        {
            if (!Grid.Contains(cell)) return false;
            foreach (var p in Pins.Values) if (!p.Removed && p.Cell == cell) return false;
            return true;
        }

        // Would moving pin to 'plane' keep every attached rope under the reject tension (0.55)?
        public bool IsWithinReach(Pin pin, Vector2 plane)
        {
            foreach (var r in pin.Ropes)
            {
                if (r.Merged) continue;
                float len = Vector2.Distance(plane, r.Other(pin).Position);
                if (len > RopeTensionRule.MaxLength(RopeTensionRule.RestLength)) return false;
            }
            return true;
        }

        public bool CanMove(Pin pin, Vector2Int to) =>
            !pin.Removed && !pin.Locked && pin.InputEnabled && pin.Cell != to && IsCellFree(to) && IsWithinReach(pin, Grid.CellToPlane(to));

        public void SetPinPosition(Pin pin, Vector2 plane)
        {
            pin.Position = plane;
            foreach (var r in pin.Ropes) r.Reshaped = true;
            RecalculateCrossings();
        }

        public void CommitMove(Pin pin, Vector2Int to, bool countMove = true)
        {
            var from = pin.Cell;
            pin.Cell = to;
            pin.Position = Grid.CellToPlane(to);
            foreach (var r in pin.Ropes) r.Reshaped = true;
            if (countMove) MovesUsed++;
            RecalculateCrossings();
            PinMoved?.Invoke(pin, from, to);
        }

        public void RecalculateCrossings()
        {
            foreach (var r in Ropes) { r.CrossingCount = 0; if (!r.Merged) r.UpdatePath(); }
            for (int i = 0; i < Ropes.Count; i++)
            {
                var a = Ropes[i];
                if (a.Merged || a.Merging) continue;
                for (int j = i + 1; j < Ropes.Count; j++)
                {
                    var b = Ropes[j];
                    if (b.Merged || b.Merging) continue;
                    if (RopeGeometry.PathsTouch(a.Path, b.Path))
                    {
                        a.CrossingCount++; b.CrossingCount++;
                    }
                }
            }
        }

        // ---- Win / merge check (call every frame) ----
        public const float SuccessCheckDelay = 0.15f;
        private float _successTimer;

        public void Tick(float dt)
        {
            if (IsWon) return;

            foreach (var r in Ropes)
            {
                if (r.Merged || r.Merging) continue;
                if (r.CrossingCount > 0) { r.FreeTimer = 0f; r.IsFree = false; continue; }
                r.FreeTimer += dt;
                if (r.FreeTimer >= SuccessCheckDelay) r.IsFree = true;
            }

            foreach (var r in Ropes)
                if (r.IsFree && !r.Merging && !r.Merged && CanMerge(r))
                    StartMerge(r);

            bool anyActive = false, anyMerging = false;
            foreach (var r in Ropes) { if (!r.Merged) anyActive = true; if (r.Merging) anyMerging = true; }

            if (anyActive || anyMerging) { _successTimer = 0f; }
            else
            {
                _successTimer += dt;
                if (_successTimer >= SuccessCheckDelay) { IsWon = true; Won?.Invoke(); return; }
            }

            if (MoveLimit > 0 && MovesUsed >= MoveLimit && anyActive && !anyMerging && !AnyFreeRope())
                OutOfMoves?.Invoke();
        }

        // At least one endpoint pin must belong only to this rope.
        private static bool CanMerge(Rope r) => r.A.ActiveRopeCount == 1 || r.B.ActiveRopeCount == 1;

        private bool AnyFreeRope() { foreach (var r in Ropes) if (!r.Merged && r.CrossingCount == 0) return true; return false; }

        private void StartMerge(Rope r)
        {
            r.Merging = true;
            // Only pins that belong solely to this rope stop accepting input
            if (r.A.ActiveRopeCount == 1) r.A.InputEnabled = false;
            if (r.B.ActiveRopeCount == 1) r.B.InputEnabled = false;
            RecalculateCrossings();   // merging rope no longer blocks others
            RopeMergeStarted?.Invoke(r);
        }

        // Call from the merge animation's OnComplete.
        public void CompleteMerge(Rope r, out bool removePinA, out bool removePinB)
        {
            r.Merging = false; r.Merged = true;
            removePinA = r.A.ActiveRopeCount == 0; if (removePinA) r.A.Removed = true;
            removePinB = r.B.ActiveRopeCount == 0; if (removePinB) r.B.Removed = true;
            RecalculateCrossings();
        }
    }
}
