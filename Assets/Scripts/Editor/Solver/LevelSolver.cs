using System.Collections.Generic;
using TwistedTangle.Editor.Geometry;
using TwistedTangle.Runtime.Data.ScriptableObjects;
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
        public int InitialCrossings;
        public int OverStretchedRopes;      // ropes that START longer than the reach limit (designer should fix)
        public int ExpandedNodes;
        public bool HitExpansionLimit;      // true => gave up at the cap; "not solvable" is then inconclusive
        public readonly List<SolveMove> Solution = new();
    }

    /// <summary>
    /// Editor-time auto-solver (see Docs/level-solver-design.md). Model = Planarity + a 3-unit reach
    /// limit. A move relocates a movable pin to an empty hole, legal only if every attached rope stays
    /// within reach; ropes don't block each other, so only the final crossing state decides resolution.
    /// Layer is a *preference*, not a rule: any rope (top or bottom) may be moved, but the search tries
    /// the top (higher-Layer) ropes first so solutions read top-down, the way a player untangles. Each
    /// rope is a straight edge between its endpoint pins (wrap-waypoints ignored). Runs in the editor only.
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

            // Per-node: neighbours (reach check), top layer (move ordering), and labels (which rope/end).
            var adjacency = new List<int>[nodeCount];
            var nodeTopLayer = new int[nodeCount];
            var nodeRopeIds = new int[nodeCount][];
            var nodePinDesc = new string[nodeCount];
            {
                var refs = new List<string>[nodeCount];
                var ids = new List<int>[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    adjacency[n] = new List<int>();
                    nodeTopLayer[n] = int.MinValue;
                    refs[n] = new List<string>();
                    ids[n] = new List<int>();
                }
                for (int e = 0; e < edges.Count; e++)
                {
                    int a = edges[e].a, b = edges[e].b, id = edgeRopeIds[e], layer = edgeLayer[e];
                    adjacency[a].Add(b); adjacency[b].Add(a);
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

            int NodeCell(int[] state, int node) =>
                movableOf[node] >= 0 ? state[movableOf[node]] : Encode(nodeCoords[node], width);

            int maxReach = options.MaxRopeReach;
            bool WithinReach(int cellA, int cellB) =>
                Mathf.Max(Mathf.Abs(cellA % width - cellB % width),
                          Mathf.Abs(cellA / width - cellB / width)) <= maxReach; // Chebyshev (king-move)

            bool ReachOk(int[] state, int node, int targetCell)
            {
                foreach (int nb in adjacency[node])
                    if (!WithinReach(targetCell, NodeCell(state, nb))) return false;
                return true;
            }

            bool EdgesShareNode(int i, int j) =>
                edges[i].a == edges[j].a || edges[i].a == edges[j].b ||
                edges[i].b == edges[j].a || edges[i].b == edges[j].b;

            bool Crosses(int[] state, int i, int j)
            {
                Vector2 p1 = Center(NodeCell(state, edges[i].a), width);
                Vector2 p2 = Center(NodeCell(state, edges[i].b), width);
                Vector2 q1 = Center(NodeCell(state, edges[j].a), width);
                Vector2 q2 = Center(NodeCell(state, edges[j].b), width);
                return CrossingSolver.SegmentsIntersect(p1, p2, q1, q2, out _, out _, out _);
            }

            int CountCrossings(int[] state)
            {
                int count = 0;
                for (int i = 0; i < edges.Count; i++)
                    for (int j = i + 1; j < edges.Count; j++)
                        if (!EdgesShareNode(i, j) && Crosses(state, i, j)) count++;
                return count;
            }

            // Movable slots that touch at least one crossing — the only pins worth moving.
            // Sorted by the pin's top layer (descending) so the search tries the top rope first.
            List<int> CrossingSlots(int[] state)
            {
                var slots = new HashSet<int>();
                for (int i = 0; i < edges.Count; i++)
                    for (int j = i + 1; j < edges.Count; j++)
                    {
                        if (EdgesShareNode(i, j) || !Crosses(state, i, j)) continue;
                        AddSlot(slots, edges[i].a); AddSlot(slots, edges[i].b);
                        AddSlot(slots, edges[j].a); AddSlot(slots, edges[j].b);
                    }
                var list = new List<int>(slots);
                list.Sort((s1, s2) =>
                    nodeTopLayer[movableNodes[s2]].CompareTo(nodeTopLayer[movableNodes[s1]]));
                return list;
            }

            void AddSlot(HashSet<int> slots, int node) { if (movableOf[node] >= 0) slots.Add(movableOf[node]); }

            // --- best-first (A*-ish) search over movable-pin placements -------------------------
            var start = new int[movableNodes.Count];
            for (int m = 0; m < movableNodes.Count; m++) start[m] = Encode(nodeCoords[movableNodes[m]], width);

            for (int e = 0; e < edges.Count; e++)
                if (!WithinReach(NodeCell(start, edges[e].a), NodeCell(start, edges[e].b)))
                    result.OverStretchedRopes++;

            result.InitialCrossings = CountCrossings(start);
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
                            H = CountCrossings(next),
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
