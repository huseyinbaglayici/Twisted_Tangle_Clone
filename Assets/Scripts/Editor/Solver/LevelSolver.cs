using System.Collections.Generic;
using TwistedTangle.Editor.Geometry;
using TwistedTangle.Runtime.Data.Enums;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Editor.Solver
{
    /// <summary>Inputs that tune a solve: the move budget, a safety cap, rope reach, and locked pins.</summary>
    public sealed class SolveOptions
    {
        /// <summary>Don't look for solutions longer than this many pin moves.</summary>
        public int MaxMoves = 30;

        /// <summary>Hard cap on expanded search nodes so the editor never hangs. Tunable.</summary>
        public int MaxExpansions = 10000;

        /// <summary>Max rope length: a move is illegal if any attached rope's two pins would end up
        /// farther apart than this. Direction-independent (Chebyshev / king-move distance).</summary>
        public int MaxRopeReach = 3;

        /// <summary>Pins (by grid cell) the designer locked — the solver never moves these. null = none.</summary>
        public HashSet<Vector2Int> LockedCells;

        /// <summary>Per-crossing over/under exceptions the designer flipped by hand. null = none.</summary>
        public HashSet<CrossingOverride> CrossingOverrides;
    }

    /// <summary>One step of a solution: move the pin at <see cref="From"/> to the empty hole <see cref="To"/>.</summary>
    public struct SolveMove
    {
        public Vector2Int From;
        public Vector2Int To;
        public int[] RopeIds;   // ropes whose endpoint sits on the moved pin (one; two if it's a shared pin)
        public string PinDesc;  // which rope(s) sit on the moved pin, e.g. "Rope 1" or "Rope 1, Rope 3"
    }

    /// <summary>Outcome of a solve attempt: whether it untangles, how, and a few diagnostics.</summary>
    public sealed class SolveResult
    {
        public bool Solvable;
        public int Moves = -1;              // length of the found solution (best-first; not guaranteed minimal)
        public int InitialCrossings;        // raw crossing count at the start
        public int InitialTangle;           // peel residual at the start (reported only; not the goal)
        public int OverStretchedRopes;      // ropes that START with a segment longer than the reach limit
        public int ExpandedNodes;
        public bool HitExpansionLimit;      // true => gave up at the cap; "not solvable" is then inconclusive
        public readonly List<SolveMove> Solution = new();
    }

    /// <summary>
    /// Editor-time auto-solver (see Docs/level-solver-design.md). A move relocates a movable endpoint
    /// pin to an empty hole; when a pin moves the whole rope follows it (bend points shift with the
    /// endpoints, "drag &amp; follow"), so the rope's shape — and which other ropes it crosses — changes.
    /// "Solved" = no rope crosses any other rope (<see cref="CrossingSolver.FindCrossings"/> is empty).
    /// Layer (via <see cref="CrossingSolver.ResolveOverUnder"/>, including braid alternation and
    /// CrossingOverrides) determines which rope is physically movable at each crossing — only the top
    /// rope can be peeled; the bottom rope is blocked beneath it. Runs in the editor only.
    /// </summary>
    public static class LevelSolver
    {
        public static SolveResult Solve(LevelDataSO level, SolveOptions options = null)
        {
            options ??= new SolveOptions();
            var result = new SolveResult();
            if (level == null || level.GridWidth <= 0 || level.GridHeight <= 0) return result;

            int width = level.GridWidth;
            int cellCount = width * level.GridHeight;
            var locked = options.LockedCells ?? new HashSet<Vector2Int>();
            int maxReach = options.MaxRopeReach;

            // Valid ropes: at least two waypoints and distinct endpoints. ropeList[p] = level rope index.
            var ropeList = new List<int>();
            for (int i = 0; i < level.Ropes.Count; i++)
            {
                var r = level.Ropes[i];
                if (r?.Path == null || r.Path.Count < 2) continue;
                if (r.Path[0].PegCoord == r.Path[^1].PegCoord) continue;
                ropeList.Add(i);
            }
            if (ropeList.Count == 0) { result.Solvable = true; result.Moves = 0; return result; }

            // --- endpoint pins become graph nodes; each valid rope links its two endpoint nodes ------
            var nodeIndex = new Dictionary<Vector2Int, int>();
            var nodeCoords = new List<Vector2Int>();
            int GetNode(Vector2Int c)
            {
                if (nodeIndex.TryGetValue(c, out int idx)) return idx;
                idx = nodeCoords.Count; nodeIndex[c] = idx; nodeCoords.Add(c); return idx;
            }

            var ropeNodeA = new int[ropeList.Count];
            var ropeNodeB = new int[ropeList.Count];
            for (int p = 0; p < ropeList.Count; p++)
            {
                var r = level.Ropes[ropeList[p]];
                // Path stores sub-grid coords; convert endpoints to coarse for the pin-move state space.
                ropeNodeA[p] = GetNode(CrossingSolver.SubToPinCoord(r.Path[0].PegCoord));
                ropeNodeB[p] = GetNode(CrossingSolver.SubToPinCoord(r.Path[^1].PegCoord));
            }
            int nodeCount = nodeCoords.Count;

            // Movable = endpoint pins the designer did NOT lock. movableOf[node] = slot index, or -1.
            var movableOf = new int[nodeCount];
            var movableNodes = new List<int>();
            for (int n = 0; n < nodeCount; n++)
            {
                if (locked.Contains(nodeCoords[n])) movableOf[n] = -1;
                else { movableOf[n] = movableNodes.Count; movableNodes.Add(n); }
            }

            // Cells permanently blocked for endpoint placement: every peg that isn't a movable node.
            // Bend points are routing-only and never block a hole.
            var movableCells = new HashSet<Vector2Int>();
            foreach (int n in movableNodes) movableCells.Add(nodeCoords[n]);
            var fixedOccupied = new HashSet<int>();
            foreach (var entity in level.GridEntities)
                if (!movableCells.Contains(entity.Coordinates))
                    fixedOccupied.Add(Encode(entity.Coordinates, width));

            // Per-node: top layer (move ordering) and labels (which rope/end) for the SolveMove output.
            var nodeTopLayer = new int[nodeCount];
            var nodeRopeIds = new int[nodeCount][];
            var nodePinDesc = new string[nodeCount];
            {
                var refs = new List<string>[nodeCount];
                var ids = new List<int>[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                { nodeTopLayer[n] = int.MinValue; refs[n] = new List<string>(); ids[n] = new List<int>(); }
                for (int p = 0; p < ropeList.Count; p++)
                {
                    var r = level.Ropes[ropeList[p]];
                    int na = ropeNodeA[p], nb = ropeNodeB[p];
                    nodeTopLayer[na] = Mathf.Max(nodeTopLayer[na], r.Layer);
                    nodeTopLayer[nb] = Mathf.Max(nodeTopLayer[nb], r.Layer);
                    refs[na].Add($"Rope {r.RopeId}"); refs[nb].Add($"Rope {r.RopeId}");
                    ids[na].Add(r.RopeId); ids[nb].Add(r.RopeId);
                }
                for (int n = 0; n < nodeCount; n++)
                { nodeRopeIds[n] = ids[n].ToArray(); nodePinDesc[n] = string.Join(", ", refs[n]); }
            }

            int NodeCell(int[] st, int node) =>
                movableOf[node] >= 0 ? st[movableOf[node]] : Encode(nodeCoords[node], width);

            // Rope shape for search: preserve bends (fixed), only move the two endpoint waypoints.
            // Bends determine the actual geometry and must be kept so FindCrossings sees the same
            // crossings as the validator does on the authored path.
            RopeData TransformRope(int p, int[] st)
            {
                var r = level.Ropes[ropeList[p]];
                Vector2Int c0 = CrossingSolver.PinToSub(Decode(NodeCell(st, ropeNodeA[p]), width));
                Vector2Int cn = CrossingSolver.PinToSub(Decode(NodeCell(st, ropeNodeB[p]), width));
                return ReshapeRope(r, c0, cn);
            }

            List<RopeData> BuildStateRopes(int[] st)
            {
                var list = new List<RopeData>(ropeList.Count);
                for (int p = 0; p < ropeList.Count; p++) list.Add(TransformRope(p, st));
                return list;
            }

            // A move is legal only when EVERY segment (endpoint→bend, bend→bend, bend→endpoint) stays
            // within maxReach coarse cells (= maxReach*SubDiv in sub-grid).
            int maxSubReach = maxReach * CrossingSolver.SubDiv;
            bool ReachOk(List<RopeData> ropes)
            {
                foreach (var r in ropes)
                    for (int k = 0; k + 1 < r.Path.Count; k++)
                    {
                        var a = r.Path[k].PegCoord;
                        var b = r.Path[k + 1].PegCoord;
                        if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) > maxSubReach) return false;
                    }
                return true;
            }

            void AddRopeSlots(HashSet<int> slots, int p)
            {
                int na = ropeNodeA[p], nb = ropeNodeB[p];
                if (movableOf[na] >= 0) slots.Add(movableOf[na]);
                if (movableOf[nb] >= 0) slots.Add(movableOf[nb]);
            }

            // Move candidates: ONLY the physically top rope at each crossing.
            // The bottom rope is trapped beneath the top rope — moving it through the crossing
            // would require threading under, creating more tangles. The top rope can be peeled
            // off freely. Layer order via ResolveOverUnder is the single source of truth for this.
            // If the top rope's endpoints are all locked, no candidate is generated for that crossing
            // (the puzzle is unsolvable at that crossing without unlocking).
            List<int> CrossingSlots(List<RopeData> stateRopes)
            {
                var crossings = CrossingSolver.FindCrossings(stateRopes);
                if (crossings.Count == 0) return new List<int>();
                var aOver = CrossingSolver.ResolveOverUnder(stateRopes, crossings, options.CrossingOverrides);
                var slots = new HashSet<int>();
                for (int c = 0; c < crossings.Count; c++)
                {
                    var x = crossings[c];
                    if (x.RopeIndexA == x.RopeIndexB) continue; // self-crossing: skip
                    // Top rope only — it can be peeled upward; bottom is physically pinned.
                    AddRopeSlots(slots, aOver[c] ? x.RopeIndexA : x.RopeIndexB);
                }
                var list = new List<int>(slots);
                list.Sort((s1, s2) =>
                    nodeTopLayer[movableNodes[s2]].CompareTo(nodeTopLayer[movableNodes[s1]]));
                return list;
            }

            // --- best-first (A*-ish) search over movable-pin placements ---------------------------
            var start = new int[movableNodes.Count];
            for (int m = 0; m < movableNodes.Count; m++) start[m] = Encode(nodeCoords[movableNodes[m]], width);

            var startRopes = BuildStateRopes(start);
            foreach (var r in startRopes)
                for (int k = 0; k + 1 < r.Path.Count; k++)
                {
                    var a = r.Path[k].PegCoord; var b = r.Path[k + 1].PegCoord;
                    if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) > maxSubReach) { result.OverStretchedRopes++; break; }
                }

            var startCrossings = CrossingSolver.FindCrossings(startRopes);
            result.InitialCrossings = CountInter(startCrossings);
            {
                var aOver = CrossingSolver.ResolveOverUnder(level.Ropes, startCrossings, options.CrossingOverrides);
                result.InitialTangle = CrossingSolver.PeelResidual(level.Ropes, startCrossings, aOver);
            }
            if (result.InitialCrossings == 0) { result.Solvable = true; result.Moves = 0; return result; }
            if (movableNodes.Count == 0) return result; // everything locked and still crossing → unsolvable

            var heap = new MinHeap();
            heap.Push(new SearchNode { State = start, G = 0, H = result.InitialCrossings });
            var visited = new HashSet<string> { Key(start) };
            int expansions = 0;

            while (heap.Count > 0)
            {
                if (expansions >= options.MaxExpansions) { result.HitExpansionLimit = true; break; }
                var cur = heap.Pop();
                expansions++;

                if (cur.H == 0)
                {
                    result.Solvable = true;
                    result.Moves = cur.G;
                    Reconstruct(cur, result.Solution);
                    break;
                }
                if (cur.G >= options.MaxMoves) continue;

                var occupied = new HashSet<int>(fixedOccupied);
                foreach (int c in cur.State) occupied.Add(c);

                var curRopes = BuildStateRopes(cur.State);
                foreach (int slot in CrossingSlots(curRopes))
                {
                    int node = movableNodes[slot];
                    int fromCell = cur.State[slot];
                    for (int cell = 0; cell < cellCount; cell++)
                    {
                        if (occupied.Contains(cell)) continue;          // hole must be empty
                        var next = (int[])cur.State.Clone();
                        next[slot] = cell;
                        if (!visited.Add(Key(next))) continue;

                        var nextRopes = BuildStateRopes(next);
                        if (!ReachOk(nextRopes)) continue;               // every segment stays within reach

                        heap.Push(new SearchNode
                        {
                            State = next,
                            G = cur.G + 1,
                            H = CountInter(CrossingSolver.FindCrossings(nextRopes)),
                            Parent = cur,
                            Move = new SolveMove
                            {
                                From = Decode(fromCell, width),
                                To = Decode(cell, width),
                                RopeIds = nodeRopeIds[node],
                                PinDesc = nodePinDesc[node]
                            }
                        });
                    }
                }
            }

            result.ExpandedNodes = expansions;
            return result;
        }

        private static void Reconstruct(SearchNode goal, List<SolveMove> into)
        {
            var stack = new Stack<SolveMove>();
            for (var n = goal; n?.Parent != null; n = n.Parent) stack.Push(n.Move);
            while (stack.Count > 0) into.Add(stack.Pop());
        }

        private sealed class SearchNode
        {
            public int[] State;
            public int G;
            public int H;
            public SearchNode Parent;
            public SolveMove Move;
        }

        /// <summary>
        /// Reshape a rope for new endpoint cells (the "drag &amp; follow" model the search explores, also
        /// used by the editor to preview a solution step). Endpoints move to <paramref name="newEndA"/>/
        /// <paramref name="newEndB"/>; each inner waypoint (bend) drags along by a displacement-blend of
        /// the two endpoint moves, weighted by its cumulative-length fraction, then snaps to the grid.
        /// Wrapping a pin never anchors the rope — it just follows like the rest of the body.
        /// </summary>
        /// <summary>
        /// Rebuilds a rope with new endpoint sub-grid positions while keeping all inner waypoints
        /// (bends and inner pins) exactly where they are. This matches the real gameplay mechanic:
        /// moving a pin only changes the rope segments directly connected to that pin; the rest of
        /// the rope stays put. A proportional-drag model was wrong — it artificially moves bends away
        /// from crossings, creating phantom solutions that don't exist in practice.
        /// </summary>
        public static RopeData ReshapeRope(RopeData rope, Vector2Int newEndA, Vector2Int newEndB)
        {
            var copy = new RopeData(rope.RopeId, rope.Tint, rope.Layer);
            int n = rope.Path.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2Int cell = k == 0 ? newEndA : k == n - 1 ? newEndB : rope.Path[k].PegCoord;
                copy.Path.Add(new RopeWaypoint(cell, rope.Path[k].Side, rope.Path[k].IsBendPoint));
            }
            return copy;
        }

        // --- small helpers -------------------------------------------------------------------

        /// <summary>Crossings between two *different* ropes (a rope crossing itself is its own shape,
        /// not a tangle the player resolves). This is the quantity the search drives to zero.</summary>
        private static int CountInter(List<RopeCrossing> crossings)
        {
            int n = 0;
            foreach (var c in crossings) if (c.RopeIndexA != c.RopeIndexB) n++;
            return n;
        }

        private static int Encode(Vector2Int c, int width) => c.y * width + c.x;
        private static Vector2Int Decode(int cell, int width) => new(cell % width, cell / width);

        private static string Key(int[] state) => string.Join(",", state);

        /// <summary>
        /// Tiny binary min-heap (Unity's runtime lacks System's PriorityQueue). Ties are broken by
        /// insertion order, so among equally-good nodes the one pushed first (a higher-layer move,
        /// since moves are generated top-layer first) is popped first.
        /// </summary>
        private sealed class MinHeap
        {
            private readonly List<(SearchNode node, int priority, int seq)> _items = new();
            private int _seq;
            public int Count => _items.Count;

            public void Push(SearchNode node)
            {
                _items.Add((node, node.G + node.H, _seq++));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(_items[i], _items[parent])) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public SearchNode Pop()
            {
                var top = _items[0].node;
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                    if (l < _items.Count && Less(_items[l], _items[smallest])) smallest = l;
                    if (r < _items.Count && Less(_items[r], _items[smallest])) smallest = r;
                    if (smallest == i) break;
                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }
                return top;
            }

            private static bool Less((SearchNode node, int priority, int seq) a,
                                     (SearchNode node, int priority, int seq) b) =>
                a.priority != b.priority ? a.priority < b.priority : a.seq < b.seq;
        }
    }
}
