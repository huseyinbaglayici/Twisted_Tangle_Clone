using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Level
{
    // Logical board state (no MonoBehaviour): which pin sits where, which pins each rope connects,
    // move count and merge state. Physics (rope shapes, contacts) lives in the views and Obi.
    public class BoardModel
    {
        // A pin may end up at most this far from every pin it is roped to.
        public const float MaxPinDistance = 2.85f;

        public class Pin
        {
            public int Id;
            public Vector2Int Cell;
            public bool Locked;
            public bool InputEnabled = true;
            public bool Removed;
            public readonly List<Rope> Ropes = new();

            public int ActiveRopeCount
            {
                get
                {
                    int n = 0;
                    foreach (var r in Ropes) if (!r.Merged) n++;
                    return n;
                }
            }
        }

        public class Rope
        {
            public int Id;
            public Pin A, B;
            public int ColorIndex;
            public bool Merging, Merged;

            public Pin Other(Pin p) => p == A ? B : A;
        }

        public readonly Dictionary<int, Pin> Pins = new();
        public readonly List<Rope> Ropes = new();
        public BoardGrid Grid { get; private set; }
        public int MoveLimit { get; private set; }
        public int MovesUsed { get; private set; }

        public void Load(StageJson stage, int moveLimit, BoardGrid grid)
        {
            Pins.Clear();
            Ropes.Clear();
            Grid = grid;
            MoveLimit = moveLimit;
            MovesUsed = 0;

            foreach (var p in stage.pins)
                Pins[p.id] = new Pin { Id = p.id, Cell = p.Cell, Locked = p.locked };

            foreach (var r in stage.ropes)
            {
                var rope = new Rope { Id = r.id, A = Pins[r.pinA], B = Pins[r.pinB], ColorIndex = r.colorIndex };
                rope.A.Ropes.Add(rope);
                rope.B.Ropes.Add(rope);
                Ropes.Add(rope);
            }
        }

        public bool IsCellFree(Vector2Int cell)
        {
            if (!Grid.Contains(cell)) return false;
            foreach (var p in Pins.Values)
                if (!p.Removed && p.Cell == cell) return false;
            return true;
        }

        public bool IsWithinPinDistance(Pin pin, Vector2Int cell)
        {
            foreach (var r in pin.Ropes)
                if (!r.Merged && Vector2Int.Distance(cell, r.Other(pin).Cell) > MaxPinDistance)
                    return false;
            return true;
        }

        public bool CanMove(Pin pin, Vector2Int to) =>
            !pin.Removed && !pin.Locked && pin.InputEnabled && pin.Cell != to && IsCellFree(to) &&
            IsWithinPinDistance(pin, to);

        public void CommitMove(Pin pin, Vector2Int to, bool countMove = true)
        {
            pin.Cell = to;
            if (countMove) MovesUsed++;
        }

        // At least one end pin must belong only to this rope.
        public static bool HasPinWithSingleRope(Rope r) => r.A.ActiveRopeCount == 1 || r.B.ActiveRopeCount == 1;

        public void BeginMerge(Rope r)
        {
            r.Merging = true;
            // Only pins that belong solely to this rope stop accepting input.
            if (r.A.ActiveRopeCount == 1) r.A.InputEnabled = false;
            if (r.B.ActiveRopeCount == 1) r.B.InputEnabled = false;
        }

        // Called when the merge animation ends; reports which pins are now free and should be removed.
        public void CompleteMerge(Rope r, out bool removePinA, out bool removePinB)
        {
            r.Merging = false;
            r.Merged = true;
            removePinA = r.A.ActiveRopeCount == 0;
            removePinB = r.B.ActiveRopeCount == 0;
            if (removePinA) r.A.Removed = true;
            if (removePinB) r.B.Removed = true;
        }
    }
}
