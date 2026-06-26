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

        /// <summary>True when the crossing is at a rope waypoint that coincides with another rope's
        /// endpoint pin. These require physical pin moves — they cannot be resolved by peeling.</summary>
        public bool IsPinCrossing;
    }

    /// <summary>
    /// Pure geometry: finds where rope paths cross and decides the default over/under order. Shared
    /// by the editor canvas and the runtime loader so a saved level renders the same in both.
    /// </summary>
    public static class CrossingSolver
    {
        private const float Eps = 1e-4f;

        /// <summary>Cell-center point for a peg/waypoint coordinate.</summary>
        public static Vector2 Center(Vector2Int coord) => new(coord.x + 0.5f, coord.y + 0.5f);

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
                        Vector2 a1 = Center(a.Path[sa].PegCoord);
                        Vector2 a2 = Center(a.Path[sa + 1].PegCoord);

                        // For self-intersection skip the same and adjacent segments (they share a peg).
                        int sbStart = i == j ? sa + 2 : 0;
                        for (int sb = sbStart; sb < bSegCount; sb++)
                        {
                            if (i == j && Mathf.Abs(sa - sb) <= 1) continue;

                            Vector2 b1 = Center(b.Path[sb].PegCoord);
                            Vector2 b2 = Center(b.Path[sb + 1].PegCoord);

                            if (!SegmentsIntersect(a1, a2, b1, b2, out float t, out float u, out Vector2 p))
                                continue;

                            result.Add(new RopeCrossing
                            {
                                RopeIndexA = i, RopeIdA = a.RopeId, SegA = sa, TA = t,
                                RopeIndexB = j, RopeIdB = b.RopeId, SegB = sb, TB = u,
                                Point = p
                            });
                        }
                    }
                }
            }

            // Pin-crossings: rope A's inner waypoint (real or virtual) coincides with rope B's endpoint
            // anchor pin. Even a virtual bend (IsBendPoint=true) routes through that exact cell, so it
            // is physically blocked by B's pin. Over/under decided by layer like any other crossing.
            for (int i = 0; i < ropes.Count; i++)
            {
                var a = ropes[i];
                if (a?.Path == null || a.Path.Count < 3) continue; // needs at least one inner waypoint

                for (int k = 1; k < a.Path.Count - 1; k++) // inner waypoints = rope body
                {
                    var pinCoord = a.Path[k].PegCoord;

                    for (int j = 0; j < ropes.Count; j++)
                    {
                        if (i == j) continue;
                        var b = ropes[j];
                        if (b?.Path == null || b.Path.Count < 2) continue;

                        bool atStart = b.Path[0].PegCoord == pinCoord;
                        bool atEnd = b.Path[^1].PegCoord == pinCoord;
                        if (!atStart && !atEnd) continue;

                        // Virtual bend: skip when both ropes leave the shared cell in the same
                        // direction — they run alongside each other, not a topological crossing.
                        if (a.Path[k].IsBendPoint)
                        {
                            Vector2 aOut = Center(a.Path[k + 1].PegCoord) - Center(pinCoord);
                            Vector2 bNextPos = atStart ? Center(b.Path[1].PegCoord) : Center(b.Path[^2].PegCoord);
                            Vector2 bOut = bNextPos - Center(pinCoord);
                            float cross = aOut.x * bOut.y - aOut.y * bOut.x;
                            if (Mathf.Abs(cross) < 0.1f && Vector2.Dot(aOut, bOut) > 0f) continue;
                        }

                        // Gap on the segment LEADING INTO the pin (k-1, t=1) so the break
                        // appears just before the pin rather than under its circle (t=0 on k).
                        result.Add(new RopeCrossing
                        {
                            RopeIndexA = i, RopeIdA = a.RopeId, SegA = k - 1, TA = 1f,
                            RopeIndexB = j, RopeIdB = b.RopeId, SegB = atStart ? 0 : b.Path.Count - 2, TB = atStart ? 0f : 1f,
                            Point = Center(pinCoord),
                            IsPinCrossing = true
                        });
                    }
                }
            }

            // Bend-on-segment crossings: rope A's inner waypoint lies strictly on rope B's segment
            // interior. Covers collinear overlap (segment-segment test returns false for parallel
            // segments) and the case where a bend sits exactly on another rope's path.
            for (int i = 0; i < ropes.Count; i++)
            {
                var a = ropes[i];
                if (a?.Path == null || a.Path.Count < 3) continue;
                for (int k = 1; k < a.Path.Count - 1; k++)
                {
                    if (a.Path[k].IsBendPoint) continue; // virtual bend — routing-only, not a physical obstacle
                    Vector2 bendPos = Center(a.Path[k].PegCoord);
                    for (int j = 0; j < ropes.Count; j++)
                    {
                        if (i == j) continue;
                        var b = ropes[j];
                        if (b?.Path == null || b.Path.Count < 2) continue;
                        for (int sb = 0; sb < b.Path.Count - 1; sb++)
                        {
                            Vector2 b1 = Center(b.Path[sb].PegCoord);
                            Vector2 b2 = Center(b.Path[sb + 1].PegCoord);
                            if (!PointOnSegmentInterior(bendPos, b1, b2, out float u)) continue;
                            result.Add(new RopeCrossing
                            {
                                RopeIndexA = i, RopeIdA = a.RopeId, SegA = k - 1, TA = 1f,
                                RopeIndexB = j, RopeIdB = b.RopeId, SegB = sb, TB = u,
                                Point = bendPos,
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
                // Pin-crossings cannot be resolved by peeling — both ropes are locked until
                // the player physically moves a pin to break the shared-coordinate constraint.
                if (crossings[c].IsPinCrossing)
                {
                    underCount[x.RopeIdA]++;
                    underCount[x.RopeIdB]++;
                }
                else
                {
                    int underId = aOver[c] ? x.RopeIdB : x.RopeIdA;
                    underCount[underId]++;
                }
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
