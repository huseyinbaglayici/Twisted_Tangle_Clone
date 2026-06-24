using System.Collections.Generic;
using TwistedTangle.Editor.Geometry;
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
        public int InitialCrossings;        // raw crossing count at the start (segment + pin crossings)
        public int InitialTangle;           // peel residual at the start: crossings left after lifting top ropes
        public int OverStretchedRopes;      // ropes that START longer than the reach limit (designer should fix)
        public int ExpandedNodes;
        public bool HitExpansionLimit;      // true => gave up at the cap; "not solvable" is then inconclusive
        public readonly List<SolveMove> Solution = new();
    }

    /// <summary>
    /// Editor-time auto-solver (see Docs/level-solver-design.md). A move relocates a movable pin to an
    /// empty hole, legal only if every attached rope stays within reach; ropes don't block each other,
    /// so only the final state decides resolution. "Solved" = the tangle is fully **peelable**: each
    /// crossing's over/under is auto-alternated by <see cref="CrossingSolver.ResolveOverUnder"/>, and a
    /// configuration is solved when every rope can be lifted off the top in turn
    /// (<see cref="CrossingSolver.PeelResidual"/> == 0) — so a braid is a real tangle but a single clean
    /// over-crossing is already separable. Layer is a *preference*: the search tries higher-Layer ropes
    /// first so solutions read top-down. Runs in the editor only.
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

            // --- nodes (endpoint pins) and edges (ropes as straight pin-to-pin segments) --------
            var nodeIndex = new Dictionary<Vector2Int, int>();
            var nodeCoords = new List<Vector2Int>();
            var edges = new List<(int a, int b)>();
            var edgeRopeIds = new List<int>();
            var edgeLayer = new List<int>();

            int GetNode(Vector2Int c)
            {
                if (nodeIndex.TryGetValue(c, out int idx)) return idx;
                idx = nodeCoords.Count;
                nodeIndex[c] = idx;
                nodeCoords.Add(c);
                return idx;
            }

            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2) continue;
                var a = rope.Path[0].PegCoord;
                var b = rope.Path[^1].PegCoord;
                if (a == b) continue;
                edges.Add((GetNode(a), GetNode(b)));
                edgeRopeIds.Add(rope.RopeId);
                edgeLayer.Add(rope.Layer);
            }

            if (edges.Count == 0) { result.Solvable = true; result.Moves = 0; return result; }
            int nodeCount = nodeCoords.Count;

            // --- all rope segments for crossing detection (bend waypoints included) ---------------
            var segRopeIdx = new List<int>();
            var segWpA     = new List<int>();
            var segWpB     = new List<int>();
            for (int ri = 0; ri < level.Ropes.Count; ri++)
            {
                var rope = level.Ropes[ri];
                if (rope?.Path == null || rope.Path.Count < 2) continue;
                for (int si = 0; si < rope.Path.Count - 1; si++)
                { segRopeIdx.Add(ri); segWpA.Add(si); segWpB.Add(si + 1); }
            }
            int segCount = segRopeIdx.Count;

            // Movable = endpoint pins the designer did NOT lock. movableOf[node] = slot index, or -1.
            var movableOf = new int[nodeCount];
            var movableNodes = new List<int>();
            for (int n = 0; n < nodeCount; n++)
            {
                if (locked.Contains(nodeCoords[n])) movableOf[n] = -1;
                else { movableOf[n] = movableNodes.Count; movableNodes.Add(n); }
            }

            // Cells permanently blocked: every peg that isn't a movable node (locked endpoints,
            // unused/decorative pegs). Movable nodes are tracked by the live state instead.
            var movableCells = new HashSet<Vector2Int>();
            foreach (int n in movableNodes) movableCells.Add(nodeCoords[n]);
            var fixedOccupied = new HashSet<int>();
            foreach (var peg in level.Pegs)
                if (!movableCells.Contains(peg.Coordinates))
                    fixedOccupied.Add(Encode(peg.Coordinates, width));

            // Per-node: top layer (move ordering) and labels (which rope/end).
            var nodeTopLayer = new int[nodeCount];
            var nodeRopeIds = new int[nodeCount][];
            var nodePinDesc = new string[nodeCount];
            {
                var refs = new List<string>[nodeCount];
                var ids = new List<int>[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    nodeTopLayer[n] = int.MinValue;
                    refs[n] = new List<string>();
                    ids[n] = new List<int>();
                }
                for (int e = 0; e < edges.Count; e++)
                {
                    int a = edges[e].a, b = edges[e].b, id = edgeRopeIds[e], layer = edgeLayer[e];
                    nodeTopLayer[a] = Mathf.Max(nodeTopLayer[a], layer);
                    nodeTopLayer[b] = Mathf.Max(nodeTopLayer[b], layer);
                    refs[a].Add($"Rope {id}"); refs[b].Add($"Rope {id}");
                    ids[a].Add(id); ids[b].Add(id);
                }
                for (int n = 0; n < nodeCount; n++)
                {
                    nodeRopeIds[n] = ids[n].ToArray();
                    nodePinDesc[n] = string.Join(", ", refs[n]);
                }
            }

            // Reach-adjacency: what must each endpoint stay within maxReach of when it moves?
            // Straight ropes (no inner waypoints): the other endpoint (dynamic, tracked in state).
            // Bent ropes: the immediately adjacent inner waypoint only — endpoint-to-endpoint
            // distance can legitimately exceed maxReach when a bend point sits between them.
            var reachNodes = new List<int>[nodeCount];   // movable node neighbours
            var reachFixed = new List<int>[nodeCount];   // static cell neighbours
            for (int n = 0; n < nodeCount; n++) { reachNodes[n] = new List<int>(); reachFixed[n] = new List<int>(); }

            void AddReach(int node, Vector2Int coord)
            {
                if (nodeIndex.TryGetValue(coord, out int rn) && movableOf[rn] >= 0)
                { if (!reachNodes[node].Contains(rn)) reachNodes[node].Add(rn); }
                else
                    reachFixed[node].Add(Encode(coord, width));
            }

            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2) continue;
                var pa = rope.Path[0].PegCoord;
                var pb = rope.Path[^1].PegCoord;
                if (pa == pb) continue;
                if (!nodeIndex.TryGetValue(pa, out int na) || !nodeIndex.TryGetValue(pb, out int nb)) continue;

                if (rope.Path.Count == 2)
                {
                    // Straight rope: both endpoints must be within reach of each other.
                    if (!reachNodes[na].Contains(nb)) reachNodes[na].Add(nb);
                    if (!reachNodes[nb].Contains(na)) reachNodes[nb].Add(na);
                }
                else
                {
                    // Bent rope: each endpoint must be within reach of its adjacent inner waypoint.
                    AddReach(na, rope.Path[1].PegCoord);
                    AddReach(nb, rope.Path[^2].PegCoord);
                }
            }

            int NodeCell(int[] state, int node) =>
                movableOf[node] >= 0 ? state[movableOf[node]] : Encode(nodeCoords[node], width);

            int maxReach = options.MaxRopeReach;
            bool WithinReach(int cellA, int cellB) =>
                Mathf.Max(Mathf.Abs(cellA % width - cellB % width),
                          Mathf.Abs(cellA / width - cellB / width)) <= maxReach; // Chebyshev (king-move)

            bool ReachOk(int[] state, int node, int targetCell)
            {
                foreach (int nb in reachNodes[node])
                    if (!WithinReach(targetCell, NodeCell(state, nb))) return false;
                foreach (int cell in reachFixed[node])
                    if (!WithinReach(targetCell, cell)) return false;
                return true;
            }

            // Cell index of a waypoint in a given state. Movable endpoints use the live state;
            // bend points and non-endpoint pegs always use their authored grid position.
            int WaypointCell(int[] st, int ropeIdx, int wpIdx)
            {
                var rope = level.Ropes[ropeIdx];
                var coord = rope.Path[wpIdx].PegCoord;
                bool isEndpoint = wpIdx == 0 || wpIdx == rope.Path.Count - 1;
                if (isEndpoint && nodeIndex.TryGetValue(coord, out int nd) && movableOf[nd] >= 0)
                    return st[movableOf[nd]];
                return Encode(coord, width);
            }

            // World-center of a waypoint. Only endpoint waypoints (first/last) can be movable;
            // bend points and middle pins stay at their authored grid position.
            Vector2 WaypointPos(int[] st, int ropeIdx, int wpIdx)
            {
                var rope = level.Ropes[ropeIdx];
                var coord = rope.Path[wpIdx].PegCoord;
                bool isEndpoint = wpIdx == 0 || wpIdx == rope.Path.Count - 1;
                if (isEndpoint && nodeIndex.TryGetValue(coord, out int nd) && movableOf[nd] >= 0)
                    return Center(st[movableOf[nd]], width);
                return Center(Encode(coord, width), width);
            }

            // Two segments share a waypoint cell → they meet at a common pin, not a crossing.
            bool SegsShareCell(int si, int sj)
            {
                var pi = level.Ropes[segRopeIdx[si]].Path;
                var pj = level.Ropes[segRopeIdx[sj]].Path;
                Vector2Int a0 = pi[segWpA[si]].PegCoord, a1 = pi[segWpB[si]].PegCoord;
                Vector2Int b0 = pj[segWpA[sj]].PegCoord, b1 = pj[segWpB[sj]].PegCoord;
                return a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1;
            }

            // Structured crossing list for a state: inter-rope segment crossings + pin-crossings
            // (a rope's body passing through another rope's live endpoint pin). Mirrors
            // CrossingSolver.FindCrossings' keys so over/under and manual overrides line up.
            List<RopeCrossing> BuildCrossings(int[] st)
            {
                var list = new List<RopeCrossing>();
                for (int i = 0; i < segCount; i++)
                    for (int j = i + 1; j < segCount; j++)
                    {
                        if (segRopeIdx[i] == segRopeIdx[j] || SegsShareCell(i, j)) continue;
                        if (!CrossingSolver.SegmentsIntersect(
                                WaypointPos(st, segRopeIdx[i], segWpA[i]),
                                WaypointPos(st, segRopeIdx[i], segWpB[i]),
                                WaypointPos(st, segRopeIdx[j], segWpA[j]),
                                WaypointPos(st, segRopeIdx[j], segWpB[j]),
                                out float t, out float u, out _)) continue;
                        list.Add(new RopeCrossing
                        {
                            RopeIndexA = segRopeIdx[i], RopeIdA = level.Ropes[segRopeIdx[i]].RopeId, SegA = segWpA[i], TA = t,
                            RopeIndexB = segRopeIdx[j], RopeIdB = level.Ropes[segRopeIdx[j]].RopeId, SegB = segWpA[j], TB = u
                        });
                    }

                for (int ri = 0; ri < level.Ropes.Count; ri++)
                {
                    var ropeA = level.Ropes[ri];
                    if (ropeA?.Path == null || ropeA.Path.Count < 3) continue;
                    for (int k = 1; k < ropeA.Path.Count - 1; k++)
                    {
                        int pinCell = Encode(ropeA.Path[k].PegCoord, width);
                        for (int rj = 0; rj < level.Ropes.Count; rj++)
                        {
                            if (ri == rj) continue;
                            var ropeB = level.Ropes[rj];
                            if (ropeB?.Path == null || ropeB.Path.Count < 2) continue;
                            bool atStart = WaypointCell(st, rj, 0) == pinCell;
                            bool atEnd = WaypointCell(st, rj, ropeB.Path.Count - 1) == pinCell;
                            if (!atStart && !atEnd) continue;
                            list.Add(new RopeCrossing
                            {
                                RopeIndexA = ri, RopeIdA = ropeA.RopeId, SegA = k, TA = 0f,
                                RopeIndexB = rj, RopeIdB = ropeB.RopeId,
                                SegB = atStart ? 0 : ropeB.Path.Count - 2, TB = atStart ? 0f : 1f
                            });
                        }
                    }
                }

                // Bend-on-segment: rope A's REAL physical inner peg lies strictly on rope B's segment
                // interior. Virtual bend points (IsBendPoint=true) are routing-only — they have no
                // physical pin, so they cannot create a resolvable crossing in the solver.
                for (int ri = 0; ri < level.Ropes.Count; ri++)
                {
                    var ropeA = level.Ropes[ri];
                    if (ropeA?.Path == null || ropeA.Path.Count < 3) continue;
                    for (int k = 1; k < ropeA.Path.Count - 1; k++)
                    {
                        if (ropeA.Path[k].IsBendPoint) continue; // virtual bend — not a physical obstacle
                        Vector2 bendPos = CrossingSolver.Center(ropeA.Path[k].PegCoord);
                        for (int rj = 0; rj < level.Ropes.Count; rj++)
                        {
                            if (ri == rj) continue;
                            var ropeB = level.Ropes[rj];
                            if (ropeB?.Path == null || ropeB.Path.Count < 2) continue;
                            for (int sb = 0; sb < ropeB.Path.Count - 1; sb++)
                            {
                                Vector2 b1 = WaypointPos(st, rj, sb);
                                Vector2 b2 = WaypointPos(st, rj, sb + 1);
                                if (!CrossingSolver.PointOnSegmentInterior(bendPos, b1, b2, out float u)) continue;
                                list.Add(new RopeCrossing
                                {
                                    RopeIndexA = ri, RopeIdA = ropeA.RopeId, SegA = k - 1, TA = 1f,
                                    RopeIndexB = rj, RopeIdB = ropeB.RopeId, SegB = sb, TB = u,
                                    Point = bendPos,
                                    IsBendOnSegment = true
                                });
                            }
                        }
                    }
                }

                return list;
            }

            // Peel residual: crossings left after lifting top ropes off (0 = separable/solved).
            // Optionally collects the tangled-core rope ids that could not be peeled.
            int TangleResidual(int[] st, HashSet<int> unpeeled = null)
            {
                var crossings = BuildCrossings(st);
                if (crossings.Count == 0) return 0;
                var aOver = CrossingSolver.ResolveOverUnder(level.Ropes, crossings, options.CrossingOverrides);
                return CrossingSolver.PeelResidual(level.Ropes, crossings, aOver, unpeeled);
            }

            // Movable slots: which pins are actually worth moving to resolve crossings?
            //
            // Standard seg-seg: either rope's endpoints can change the crossing → add both.
            // Pin-crossing (TA≈0): A's inner waypoint is fixed — only the SPECIFIC B endpoint
            //   that is AT the crossing point can resolve it (moving B's other endpoint does nothing).
            // Bend-on-segment (IsBendOnSegment): A's bend is fixed — only B's endpoints can
            //   move the segment away from the bend → add all of B's endpoints.
            List<int> CrossingSlots(int[] st)
            {
                var crossings = BuildCrossings(st);
                var involvedIdxs = new HashSet<int>();
                var slots = new HashSet<int>();

                foreach (var c in crossings)
                {
                    bool isPinCrossing = c.TA < 1e-3f; // TA==0 set by pin-crossing detection
                    if (isPinCrossing)
                    {
                        // Moving A's endpoints can't shift A's fixed inner waypoint.
                        // Only the SPECIFIC B endpoint at the crossing matters.
                        var ropeB = level.Ropes[c.RopeIndexB];
                        Vector2Int bEp = c.TB < 0.5f ? ropeB.Path[0].PegCoord : ropeB.Path[^1].PegCoord;
                        if (nodeIndex.TryGetValue(bEp, out int nd) && movableOf[nd] >= 0)
                            slots.Add(movableOf[nd]);
                    }
                    else if (c.IsBendOnSegment)
                        involvedIdxs.Add(c.RopeIndexB); // A's bend fixed, B's segment can move
                    else
                    { involvedIdxs.Add(c.RopeIndexA); involvedIdxs.Add(c.RopeIndexB); }
                }

                foreach (int ri in involvedIdxs) AddRopeSlots(slots, ri);
                var list = new List<int>(slots);
                list.Sort((s1, s2) =>
                    nodeTopLayer[movableNodes[s2]].CompareTo(nodeTopLayer[movableNodes[s1]]));
                return list;
            }

            void AddRopeSlots(HashSet<int> slots, int ropeIdx)
            {
                var rope = level.Ropes[ropeIdx];
                if (nodeIndex.TryGetValue(rope.Path[0].PegCoord, out int na) && movableOf[na] >= 0)
                    slots.Add(movableOf[na]);
                if (nodeIndex.TryGetValue(rope.Path[^1].PegCoord, out int nb) && movableOf[nb] >= 0)
                    slots.Add(movableOf[nb]);
            }

            // --- best-first (A*-ish) search over movable-pin placements -------------------------
            var start = new int[movableNodes.Count];
            for (int m = 0; m < movableNodes.Count; m++) start[m] = Encode(nodeCoords[movableNodes[m]], width);

            // Check per-segment reach (not endpoint-to-endpoint): a bent rope can have endpoints
            // farther apart than maxReach while each individual segment stays within reach.
            for (int si = 0; si < segCount; si++)
                if (!WithinReach(WaypointCell(start, segRopeIdx[si], segWpA[si]),
                                 WaypointCell(start, segRopeIdx[si], segWpB[si])))
                    result.OverStretchedRopes++;

            result.InitialCrossings = BuildCrossings(start).Count;
            result.InitialTangle = TangleResidual(start);
            if (result.InitialCrossings == 0) { result.Solvable = true; result.Moves = 0; return result; }
            if (movableNodes.Count == 0) return result; // everything locked and still tangled → unsolvable

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

                // Top ropes first (CrossingSlots is layer-sorted); ties in the heap keep that order,
                // so an equally-good solution that starts from the top is the one returned.
                foreach (int slot in CrossingSlots(cur.State))
                {
                    int node = movableNodes[slot];
                    int fromCell = cur.State[slot];
                    for (int cell = 0; cell < cellCount; cell++)
                    {
                        if (occupied.Contains(cell)) continue;        // hole must be empty
                        if (!ReachOk(cur.State, node, cell)) continue; // every attached rope stays within reach
                        var next = (int[])cur.State.Clone();
                        next[slot] = cell;
                        if (!visited.Add(Key(next))) continue;

                        heap.Push(new SearchNode
                        {
                            State = next,
                            G = cur.G + 1,
                            H = BuildCrossings(next).Count,
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

        // --- small helpers -------------------------------------------------------------------
        private static int Encode(Vector2Int c, int width) => c.y * width + c.x;
        private static Vector2Int Decode(int cell, int width) => new(cell % width, cell / width);
        private static Vector2 Center(int cell, int width) => new(cell % width + 0.5f, cell / width + 0.5f);

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
