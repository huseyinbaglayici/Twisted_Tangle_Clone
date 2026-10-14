using System.Collections.Generic;
using UnityEngine;

namespace Editor.Geometry
{
    // Editor stand-in for the physical crossing test. Ropes are slack and bulge sideways: a straight pin-to-pin
    // model misjudges ~31% of rope pairs, the authored path with the end segments skipped only ~4%.
    public static class RopeGeometry
    {
        public const float TouchDistance = 0.15f;

        public static bool PathsTouch(Vector2[] p, Vector2[] q, float threshold = TouchDistance)
        {
            int skipP = p.Length > 3 ? 1 : 0, skipQ = q.Length > 3 ? 1 : 0;
            for (int i = skipP; i < p.Length - 1 - skipP; i++)
            for (int j = skipQ; j < q.Length - 1 - skipQ; j++)
                if (SegmentDistance(p[i], p[i + 1], q[j], q[j + 1]) < threshold)
                    return true;
            return false;
        }

        public static float SegmentDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
            if (d1 * d2 < 0f && d3 * d4 < 0f) return 0f;
            return Mathf.Min(Mathf.Min(PointSeg(p1, q1, q2), PointSeg(p2, q1, q2)),
                             Mathf.Min(PointSeg(q1, p1, p2), PointSeg(q2, p1, p2)));
        }

        private static float PointSeg(Vector2 x, Vector2 a, Vector2 b)
        {
            var d = b - a; float len2 = d.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(x - a, d) / len2) : 0f;
            return (a + d * t - x).magnitude;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        public static float PathLength(Vector2[] p)
        {
            float l = 0f; for (int i = 0; i < p.Length - 1; i++) l += Vector2.Distance(p[i], p[i + 1]); return l;
        }

        // Crossings as fractional point indices (segment + t), sorted along p. The half-open sign test counts a
        // crossing through a shared path point once (grid-aligned ropes hit points exactly); crossings at a pin don't count.
        public static List<(float onP, float onQ)> Crossings(Vector2[] p, Vector2[] q)
        {
            const float pinRadius = 0.05f;
            var result = new List<(float, float)>();
            for (int i = 0; i < p.Length - 1; i++)
            for (int j = 0; j < q.Length - 1; j++)
            {
                float d1 = Cross(q[j], q[j + 1], p[i]), d2 = Cross(q[j], q[j + 1], p[i + 1]);
                float d3 = Cross(p[i], p[i + 1], q[j]), d4 = Cross(p[i], p[i + 1], q[j + 1]);
                if (d1 > 0f == d2 > 0f || d3 > 0f == d4 > 0f) continue;
                float t = d1 / (d1 - d2);
                var x = Vector2.Lerp(p[i], p[i + 1], t);
                if (Vector2.Distance(x, p[0]) < pinRadius || Vector2.Distance(x, p[^1]) < pinRadius ||
                    Vector2.Distance(x, q[0]) < pinRadius || Vector2.Distance(x, q[^1]) < pinRadius) continue;
                result.Add((i + t, j + d3 / (d3 - d4)));
            }
            result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return result;
        }
    }

    // Cheap stand-in for rope physics when a pin moves (the real rope re-simulates with Obi):
    // map the authored path onto the new endpoints (rotate+scale along the chord) and scale the sideways bulge
    // by remaining slack: taut rope -> straight, slack rope -> keeps its bulge.
    public static class RopeShape
    {
        public static void Remap(Vector2[] basePath, float basePathLength, Vector2 a, Vector2 b, Vector2[] result)
        {
            int n = basePath.Length;
            Vector2 a0 = basePath[0], b0 = basePath[n - 1];
            Vector2 c0 = b0 - a0, c1 = b - a;
            float l0 = c0.magnitude, l1 = c1.magnitude;
            if (l0 < 1e-4f || l1 < 1e-4f) { for (int i = 0; i < n; i++) result[i] = Vector2.Lerp(a, b, i / (n - 1f)); return; }

            Vector2 u0 = c0 / l0, v0 = new(-u0.y, u0.x);
            Vector2 u1 = c1 / l1, v1 = new(-u1.y, u1.x);
            float slack0 = Mathf.Max(0.0001f, basePathLength - l0);
            float slack1 = Mathf.Max(0f, basePathLength - l1);
            float bulge = Mathf.Sqrt(Mathf.Clamp01(slack1 / slack0)) * Mathf.Sqrt(l1 / l0); // parabola: h ~ sqrt(c*(L-c))

            for (int i = 0; i < n; i++)
            {
                var d = basePath[i] - a0;
                float along = Vector2.Dot(d, u0) / l0;
                float side = Vector2.Dot(d, v0);
                result[i] = a + u1 * (along * l1) + v1 * (side * bulge);
            }
        }

        public static Vector2[] Straight(Vector2 a, Vector2 b, int points = 13)
        {
            var r = new Vector2[points];
            for (int i = 0; i < points; i++) r[i] = Vector2.Lerp(a, b, i / (points - 1f));
            return r;
        }
    }

    // Every rope uses the same Obi blueprint: restLength = 2.5 => max pin distance ~4.22, red at ~3.91.
    // Tension = length/restLength - 1; percentage = clamp01(tension / 1.25).
    // >= 0.45 => rope tinted red, >= 0.55 => drag rejected.
    public static class TensionLimits
    {
        public const float RestLength = 2.5f;
        public const float MaxTension = 1.25f;
        public const float WarnPercentage = 0.45f;
        public const float RejectPercentage = 0.55f;

        public static float Percentage(float length, float restLength)
        {
            if (restLength <= 0.0001f) return 0f;
            float tension = length / restLength - 1f;
            return Mathf.Clamp01(tension / MaxTension);
        }

        public static float MaxLength(float restLength) => restLength * (1f + RejectPercentage * MaxTension);
    }
}
