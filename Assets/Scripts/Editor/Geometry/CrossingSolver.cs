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
