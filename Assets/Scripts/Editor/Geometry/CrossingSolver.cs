using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Editor.Geometry
{
    /// <summary>
    /// A point where two rope segments cross. Coordinates are in grid cell-center space (peg at
    /// (x,y) maps to (x+0.5, y+0.5)), so crossings are resolution-independent. <see cref="SegA"/>/
    /// <see cref="SegB"/> are segment indices within each rope (segment i spans path waypoints i..i+1).
    /// This is editor/tool-only geometry; runtime stays pure data.
    /// </summary>
    public struct RopeCrossing
    {
        public int RopeIndexA;
        public int RopeIdA;
        public int SegA;
        public float TA;

        public int RopeIndexB;
        public int RopeIdB;
        public int SegB;
        public float TB;

        public Vector2 Point;

        /// <summary>True when RopeA's fixed bend point lies on RopeB's segment.
        /// Moving RopeA's endpoints cannot resolve this — only RopeB's endpoints can.</summary>
        public bool IsBendOnSegment;
    }

    /// <summary>
    /// Pure geometry: finds where rope paths cross and decides the default over/under order. Shared
    /// by the editor canvas and the runtime loader so a saved level renders the same in both.
    /// </summary>
    public static class CrossingSolver
    {
        private const float Eps = 1e-4f;

        /// <summary>Sub-grid divisions per coarse cell. Each coarse cell is divided into SubDiv×SubDiv sub-cells.</summary>
        public const int SubDiv = 3;

        /// <summary>Cell-center point for a pin/entity coordinate (coarse grid).</summary>
        public static Vector2 Center(Vector2Int coord) => new(coord.x + 0.5f, coord.y + 0.5f);

        /// <summary>Center point for a rope waypoint in sub-grid coordinates.</summary>
        public static Vector2 SubCenter(Vector2Int subCoord) =>
            new((subCoord.x + 0.5f) / SubDiv, (subCoord.y + 0.5f) / SubDiv);

        /// <summary>Converts a coarse pin coordinate to its sub-grid equivalent (pin sits at sub-cell index 1 of each axis).</summary>
        public static Vector2Int PinToSub(Vector2Int pinCoord) =>
            new(pinCoord.x * SubDiv + SubDiv / 2, pinCoord.y * SubDiv + SubDiv / 2);

        /// <summary>True if the sub-grid coord is at a pin center position (i.e. index 1 within its coarse cell on both axes).</summary>
        public static bool IsSubGridPin(Vector2Int subCoord) =>
            subCoord.x % SubDiv == SubDiv / 2 && subCoord.y % SubDiv == SubDiv / 2;

        /// <summary>Converts a sub-grid pin coord back to the coarse pin coord. Only valid when IsSubGridPin is true.</summary>
        public static Vector2Int SubToPinCoord(Vector2Int subCoord) =>
            new((subCoord.x - SubDiv / 2) / SubDiv, (subCoord.y - SubDiv / 2) / SubDiv);

        /// <summary>(segment index, t) that represents being AT waypoint <paramref name="waypointIndex"/>:
        /// the first waypoint uses its outgoing segment (t=0); every other waypoint breaks the segment
        /// leading into it (t=1), so a render gap appears just before the waypoint, not under its pin.</summary>
        public static void WaypointSegT(int waypointIndex, out int seg, out float t)
        {
            if (waypointIndex == 0) { seg = 0; t = 0f; }
            else { seg = waypointIndex - 1; t = 1f; }
        }

        /// <summary>
        /// All crossings among the given ropes, including a rope crossing itself. Segments that only
        /// touch at a shared peg (a common endpoint) are not counted as crossings.
        /// </summary>
        public static List<RopeCrossing> FindCrossings(IReadOnlyList<RopeData> ropes)
        {
            var result = new List<RopeCrossing>();
            if (ropes == null) return result;

            for (int i = 0; i < ropes.Count; i++)
            {
                var a = ropes[i];
                if (a?.Path == null || a.Path.Count < 2) continue;

                for (int j = i; j < ropes.Count; j++)
                {
                    var b = ropes[j];
                    if (b?.Path == null || b.Path.Count < 2) continue;

                    int aSegCount = a.Path.Count - 1;
                    int bSegCount = b.Path.Count - 1;

                    for (int sa = 0; sa < aSegCount; sa++)
                    {
                        Vector2 a1 = SubCenter(a.Path[sa].PegCoord);
                        Vector2 a2 = SubCenter(a.Path[sa + 1].PegCoord);

                        // For self-intersection skip the same and adjacent segments (they share a peg).
                        int sbStart = i == j ? sa + 2 : 0;
                        for (int sb = sbStart; sb < bSegCount; sb++)
                        {
                            if (i == j && Mathf.Abs(sa - sb) <= 1) continue;

                            Vector2 b1 = SubCenter(b.Path[sb].PegCoord);
                            Vector2 b2 = SubCenter(b.Path[sb + 1].PegCoord);

                            if (SegmentsIntersect(a1, a2, b1, b2, out float t, out float u, out Vector2 p))
                            {
                                result.Add(new RopeCrossing
                                {
                                    RopeIndexA = i, RopeIdA = a.RopeId, SegA = sa, TA = t,
                                    RopeIndexB = j, RopeIdB = b.RopeId, SegB = sb, TB = u,
                                    Point = p
                                });
                                continue;
                            }

                            // Collinear overlap: two segments from different ropes lying along the same
                            // line and overlapping on an interval (not merely touching at a point) are
                            // physically on top of each other. Skip when they share a waypoint cell — that
                            // is already a shared-cell crossing — to avoid double-counting.
                            if (i != j &&
                                !SegmentsShareCell(a, sa, b, sb) &&
                                SegmentsOverlapCollinear(a1, a2, b1, b2, out float ot, out float ou, out Vector2 omid))
                            {
                                result.Add(new RopeCrossing
                                {
                                    RopeIndexA = i, RopeIdA = a.RopeId, SegA = sa, TA = ot,
                                    RopeIndexB = j, RopeIdB = b.RopeId, SegB = sb, TB = ou,
                                    Point = omid
                                });
                            }
                        }
                    }
                }
            }

            // Shared-cell crossings: any cell where two different ropes each have a waypoint is a
            // crossing — the ropes physically overlap there, so one passes over the other (over/under
            // decided by layer like any segment crossing, and resolved by peeling). The sole exception
            // is a cell that is a terminal endpoint for BOTH ropes: there they are simply tied to the
            // same shared pin, not crossed. This covers a rope's body passing through another rope's
            // pin AND two ropes that bend through the same cell (e.g. a pinwheel of bent ropes).
            for (int i = 0; i < ropes.Count; i++)
            {
                var a = ropes[i];
                if (a?.Path == null || a.Path.Count < 2) continue;

                for (int j = i + 1; j < ropes.Count; j++)
                {
                    var b = ropes[j];
                    if (b?.Path == null || b.Path.Count < 2) continue;

                    for (int ka = 0; ka < a.Path.Count; ka++)
                    {
                        var cell = a.Path[ka].PegCoord;
                        for (int kb = 0; kb < b.Path.Count; kb++)
                        {
                            if (b.Path[kb].PegCoord != cell) continue;

                            bool aTerminal = ka == 0 || ka == a.Path.Count - 1;
                            bool bTerminal = kb == 0 || kb == b.Path.Count - 1;
                            if (aTerminal && bTerminal) continue; // shared pin — joined, not crossed

                            WaypointSegT(ka, out int segA, out float tA);
                            WaypointSegT(kb, out int segB, out float tB);
                            result.Add(new RopeCrossing
                            {
                                RopeIndexA = i, RopeIdA = a.RopeId, SegA = segA, TA = tA,
                                RopeIndexB = j, RopeIdB = b.RopeId, SegB = segB, TB = tB,
                                Point = SubCenter(cell)
                            });
                        }
                    }
                }
            }

            // Waypoint-on-segment crossings: any of rope A's waypoints (endpoint, real peg or virtual
            // bend) lying strictly on rope B's segment interior means the ropes physically touch there.
            // The waypoint sits at a B cell with no B waypoint of its own, so the shared-cell test above
            // misses it and (for parallel segments) so does the interior segment test.
            for (int i = 0; i < ropes.Count; i++)
            {
                var a = ropes[i];
                if (a?.Path == null || a.Path.Count < 2) continue;
                for (int k = 0; k < a.Path.Count; k++)
                {
                    var aCell = a.Path[k].PegCoord;
                    Vector2 wpPos = SubCenter(aCell);
                    int segA = k == 0 ? 0 : k - 1;
                    float tA = k == 0 ? 0f : 1f;
                    for (int j = 0; j < ropes.Count; j++)
                    {
                        if (i == j) continue;
                        var b = ropes[j];
                        if (b?.Path == null || b.Path.Count < 2) continue;
                        for (int sb = 0; sb < b.Path.Count - 1; sb++)
                        {
                            // Skip if A's waypoint coincides with one of this B segment's waypoint cells
                            // (that is a shared-cell crossing, already handled above).
                            if (aCell == b.Path[sb].PegCoord || aCell == b.Path[sb + 1].PegCoord) continue;
                            Vector2 b1 = SubCenter(b.Path[sb].PegCoord);
                            Vector2 b2 = SubCenter(b.Path[sb + 1].PegCoord);
                            if (!PointOnSegmentInterior(wpPos, b1, b2, out float u)) continue;
                            result.Add(new RopeCrossing
                            {
                                RopeIndexA = i, RopeIdA = a.RopeId, SegA = segA, TA = tA,
                                RopeIndexB = j, RopeIdB = b.RopeId, SegB = sb, TB = u,
                                Point = wpPos,
                                IsBendOnSegment = true
                            });
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Whether rope A draws on top of rope B at a crossing. Default order is by layer (higher on
        /// top), tie-broken by rope id so it is always deterministic; a registered override flips it.
        /// </summary>
        public static bool IsAOver(RopeData a, RopeData b, bool overridden)
        {
            bool aOverByDefault = a.Layer != b.Layer ? a.Layer > b.Layer : a.RopeId > b.RopeId;
            return overridden ? !aOverByDefault : aOverByDefault;
        }

        /// <summary>
        /// Per-crossing over/under for a whole tangle, returned index-aligned to <paramref name="crossings"/>
        /// (true = RopeIdA is on top). This is the single source of truth shared by the canvas renderer,
        /// the validator and the solver, so the weave you see is exactly the weave that is solved.
        /// Within each rope-pair the crossings alternate: the first (by position along the lower-id rope)
        /// is seeded by Layer, each subsequent one flips — this produces genuine braid tangles instead of
        /// always-peelable stacks. A <see cref="CrossingOverride"/> flips a specific crossing as a
        /// manual exception applied on top of the alternation.
        /// </summary>
        public static bool[] ResolveOverUnder(IReadOnlyList<RopeData> ropes,
            List<RopeCrossing> crossings, ICollection<CrossingOverride> overrides)
        {
            int n = crossings?.Count ?? 0;
            var aOver = new bool[n];
            if (n == 0) return aOver;

            var ropeById = new Dictionary<int, RopeData>();
            foreach (var r in ropes)
                if (r != null) ropeById[r.RopeId] = r;

            // Group by unordered rope-id pair; within each group sort along the lower-id rope's path
            // (seg * big + t) so the alternation follows the weave order physically.
            var pairGroups = new Dictionary<(int lo, int hi), List<(int crossingIdx, float sortKey)>>();
            for (int c = 0; c < n; c++)
            {
                var x = crossings[c];
                int lo = System.Math.Min(x.RopeIdA, x.RopeIdB);
                int hi = System.Math.Max(x.RopeIdA, x.RopeIdB);
                var key = (lo, hi);
                if (!pairGroups.TryGetValue(key, out var list)) pairGroups[key] = list = new List<(int, float)>();
                float sortKey = x.RopeIdA == lo ? x.SegA * 10000f + x.TA : x.SegB * 10000f + x.TB;
                list.Add((c, sortKey));
            }

            foreach (var kv in pairGroups)
            {
                var group = kv.Value;
                group.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

                ropeById.TryGetValue(kv.Key.lo, out var rLo);
                ropeById.TryGetValue(kv.Key.hi, out var rHi);
                bool loOverHi = IsAOver(rLo, rHi, false);

                for (int i = 0; i < group.Count; i++)
                {
                    int c = group[i].crossingIdx;
                    var x = crossings[c];
                    bool thisLoOver = (i % 2 == 0) ? loOverHi : !loOverHi; // alternate per weave order
                    bool val = x.RopeIdA == kv.Key.lo ? thisLoOver : !thisLoOver;
                    if (overrides != null &&
                        overrides.Contains(CrossingOverride.Create(x.RopeIdA, x.SegA, x.RopeIdB, x.SegB)))
                        val = !val;
                    aOver[c] = val;
                }
            }

            return aOver;
        }

        /// <summary>
        /// How tangled a configuration is, given a per-crossing over/under map (see
        /// <see cref="ResolveOverUnder"/>). Models the real-game mechanic of peeling the top rope off:
        /// repeatedly remove any rope that is on top at *all* of its remaining crossings (or has none),
        /// dropping its crossings; return how many crossings are left when nothing more can be peeled.
        /// 0 = fully separable (solved). Self-crossings are ignored (a rope can't peel off itself).
        /// The process is confluent, so the peel order does not affect the result.
        /// </summary>
        /// <param name="unpeeled">If non-null, filled with the ids of ropes that still have an active
        /// crossing when peeling stalls — i.e. the tangled core worth moving.</param>
        public static int PeelResidual(IReadOnlyList<RopeData> ropes,
            List<RopeCrossing> crossings, bool[] aOver, HashSet<int> unpeeled = null)
        {
            int n = crossings?.Count ?? 0;
            if (n == 0) return 0;

            var active = new bool[n];
            var underCount = new Dictionary<int, int>();      // ropeId -> # active crossings it's UNDER at
            var ropeCrossings = new Dictionary<int, List<int>>();
            int activeCount = 0;

            void Ensure(int id)
            {
                if (underCount.ContainsKey(id)) return;
                underCount[id] = 0;
                ropeCrossings[id] = new List<int>();
            }

            for (int c = 0; c < n; c++)
            {
                var x = crossings[c];
                if (x.RopeIdA == x.RopeIdB) continue;          // self-crossing: not a link to peel
                active[c] = true; activeCount++;
                Ensure(x.RopeIdA); Ensure(x.RopeIdB);
                ropeCrossings[x.RopeIdA].Add(c);
                ropeCrossings[x.RopeIdB].Add(c);
                int underId = aOver[c] ? x.RopeIdB : x.RopeIdA;
                underCount[underId]++;
            }

            var ids = new List<int>(ropeCrossings.Keys);
            var peeled = new HashSet<int>();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (int id in ids)
                {
                    if (peeled.Contains(id) || underCount[id] != 0) continue; // under somewhere → can't lift
                    peeled.Add(id);
                    progress = true;
                    foreach (int c in ropeCrossings[id])
                    {
                        if (!active[c]) continue;
                        active[c] = false; activeCount--;
                        var x = crossings[c];
                        int underId = aOver[c] ? x.RopeIdB : x.RopeIdA; // the partner that was under
                        if (!peeled.Contains(underId)) underCount[underId]--;
                    }
                }
            }

            if (unpeeled != null)
                foreach (int id in ids)
                    if (!peeled.Contains(id)) unpeeled.Add(id);

            return activeCount;
        }

        /// <summary>True if point p lies strictly in the interior of segment q1→q2 (not at the endpoints).</summary>
        public static bool PointOnSegmentInterior(Vector2 p, Vector2 q1, Vector2 q2, out float u)
        {
            u = 0f;
            Vector2 d = q2 - q1;
            float lenSq = d.sqrMagnitude;
            if (lenSq < Eps) return false;
            Vector2 g = p - q1;
            float cross = d.x * g.y - d.y * g.x;
            if (Mathf.Abs(cross) > Eps * Mathf.Sqrt(lenSq)) return false; // not collinear
            u = (g.x * d.x + g.y * d.y) / lenSq;
            return u > Eps && u < 1f - Eps;
        }

        /// <summary>True if two rope segments share a waypoint grid cell (a common pin/bend cell).</summary>
        private static bool SegmentsShareCell(RopeData a, int sa, RopeData b, int sb)
        {
            var a0 = a.Path[sa].PegCoord; var a1 = a.Path[sa + 1].PegCoord;
            var b0 = b.Path[sb].PegCoord; var b1 = b.Path[sb + 1].PegCoord;
            return a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1;
        }

        /// <summary>
        /// True when segments p1→p2 and p3→p4 are collinear and overlap along an *interval* (more than a
        /// single touch point). <paramref name="ta"/>/<paramref name="tb"/> are the parameters of the
        /// overlap midpoint along each segment; <paramref name="mid"/> is that midpoint.
        /// </summary>
        public static bool SegmentsOverlapCollinear(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
            out float ta, out float tb, out Vector2 mid)
        {
            ta = tb = 0f; mid = default;
            Vector2 d = p2 - p1;
            float len2 = d.sqrMagnitude;
            if (len2 < Eps) return false;

            float tol = Eps * Mathf.Sqrt(len2);
            float n3 = d.x * (p3.y - p1.y) - d.y * (p3.x - p1.x);
            float n4 = d.x * (p4.y - p1.y) - d.y * (p4.x - p1.x);
            if (Mathf.Abs(n3) > tol || Mathf.Abs(n4) > tol) return false; // p3/p4 not on p1→p2's line

            float t3 = Vector2.Dot(p3 - p1, d) / len2;
            float t4 = Vector2.Dot(p4 - p1, d) / len2;
            float lo = Mathf.Max(0f, Mathf.Min(t3, t4));
            float hi = Mathf.Min(1f, Mathf.Max(t3, t4));
            if (hi - lo <= Eps) return false; // disjoint or touching at a single point

            ta = (lo + hi) * 0.5f;
            mid = p1 + ta * d;
            Vector2 e = p4 - p3;
            float elen2 = e.sqrMagnitude;
            tb = elen2 > Eps ? Vector2.Dot(mid - p3, e) / elen2 : 0f;
            return true;
        }

        /// <summary>
        /// Segment-segment intersection. Returns true only for a strictly interior crossing (both
        /// parameters in (0,1)), which excludes shared-endpoint touches and parallel/collinear lines.
        /// </summary>
        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
            out float t, out float u, out Vector2 point)
        {
            t = u = 0f;
            point = default;

            Vector2 d = p2 - p1;
            Vector2 e = p4 - p3;
            float denom = d.x * e.y - d.y * e.x;
            if (Mathf.Abs(denom) < Eps) return false; // parallel or degenerate

            Vector2 g = p3 - p1;
            t = (g.x * e.y - g.y * e.x) / denom;
            u = (g.x * d.y - g.y * d.x) / denom;

            if (t <= Eps || t >= 1f - Eps || u <= Eps || u >= 1f - Eps) return false;

            point = p1 + t * d;
            return true;
        }
    }
}
