using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Level
{
    // Logical board state (no MonoBehaviour): which pin sits where, which pins each rope connects,
    // and merge state. Physics (rope shapes, contacts) lives in the views and Obi.
    public class BoardModel
    {
        // A dragged pin is clamped this far from every pin it is roped to.
        public const float MaxPinDistance = 2.85f;
        // A slot is reachable when the clamped pin can still snap onto it (clamp + drop snap radius), e.g. the
        // first tutorial move ends 3.16 away. Anything that ends up over-stretched is taken back by the tension check.
        public const float MaxSlotDistance = 3.6f;

        public class Pin
        {
            public int Id;
            public Vector2Int Cell;
            public bool Locked;
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
        private readonly List<Pin> _groupPins = new();
        public BoardGrid Grid { get; private set; }

        public void Load(StageJson stage, BoardGrid grid)
        {
            Pins.Clear();
            Ropes.Clear();
            Grid = grid;

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
                if (!r.Merged && Vector2Int.Distance(cell, r.Other(pin).Cell) > MaxSlotDistance)
                    return false;
            return true;
        }

        // Locked (nailed) pins never move. While a rope merges, every pin of it and of the ropes chained to it
        // through shared pins is frozen too; they all follow it in the same merge chain.
        public bool CanControl(Pin pin) =>
            pin != null && !pin.Removed && !pin.Locked && !IsMergeBusy(pin);

        public bool CanMove(Pin pin, Vector2Int to) =>
            CanControl(pin) && pin.Cell != to && IsCellFree(to) && IsWithinPinDistance(pin, to);

        // True while any rope reachable from this pin through shared pins is merging.
        public bool IsMergeBusy(Pin pin)
        {
            _groupPins.Clear();
            _groupPins.Add(pin);
            for (int i = 0; i < _groupPins.Count; i++)
            foreach (var r in _groupPins[i].Ropes)
            {
                if (r.Merged) continue;
                if (r.Merging) return true;
                if (!_groupPins.Contains(r.A)) _groupPins.Add(r.A);
                if (!_groupPins.Contains(r.B)) _groupPins.Add(r.B);
            }
            return false;
        }

        public void CommitMove(Pin pin, Vector2Int to) => pin.Cell = to;

        // At least one end pin must belong only to this rope.
        public static bool HasPinWithSingleRope(Rope r) => r.A.ActiveRopeCount == 1 || r.B.ActiveRopeCount == 1;

        public void BeginMerge(Rope r) => r.Merging = true;

        // The pin that jumps: the first end (A, then B) that holds only this rope. The other end is the target.
        public static Pin MergeSource(Rope r) => r.A.ActiveRopeCount == 1 ? r.A : r.B;

        // The target disappears with the rope unless another rope still holds it.
        public static bool IsLastRopeOf(Pin pin, Rope r)
        {
            foreach (var other in pin.Ropes)
                if (other != r && !other.Merged) return false;
            return true;
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
