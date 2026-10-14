using System;
using System.Collections.Generic;
using Runtime.Level;
using UnityEngine;

namespace Editor.Geometry
{
    // Builds the 25-point start path of a rope: a sideways arc between its pins (slack ropes bulge) and a
    // height profile like the authored levels: 0.2 at the pins, ~0.1 lying on the board, +0.2 over each rope below.
    public static class RopePathBuilder
    {
        public const int PointCount = 25;
        public const float PinHeight = 0.2f;
        public const float BoardHeight = 0.1f;
        public const float Lift = 0.2f;
        private const float NearPinLift = 0.2f, NearPinRange = 0.5f;
        private const float BumpRadius = 0.45f;
        private const float MinPathLength = 2.4f, MaxBulge = 1f, BoardMargin = 0.3f;

        // Returns the id of the rope on top at a crossing, given its rank along rope a and along rope b.
        public delegate int TopRope(RopeJson a, RopeJson b, int rankAlongA, int rankAlongB);

        public static void BuildStage(StageJson stage, Func<RopeJson, float> bendOf = null, TopRope topOf = null)
        {
            foreach (var rope in stage.ropes)
                rope.path = Arc(stage, rope, bendOf?.Invoke(rope) ?? 0f);

            topOf ??= (a, b, _, _) => a.layer != b.layer ? (a.layer > b.layer ? a.id : b.id) : Mathf.Max(a.id, b.id);
            for (int i = 0; i < stage.ropes.Count; i++)
            for (int j = i + 1; j < stage.ropes.Count; j++)
            {
                RopeJson a = stage.ropes[i], b = stage.ropes[j];
                var crossings = RopeGeometry.Crossings(Xy(a.path), Xy(b.path));
                var alongB = new List<float>();
                foreach (var c in crossings) alongB.Add(c.onQ);
                alongB.Sort();
                for (int k = 0; k < crossings.Count; k++)
                {
                    var (onA, onB) = crossings[k];
                    bool aOnTop = topOf(a, b, k, alongB.IndexOf(onB)) == a.id;
                    if (aOnTop) PlaceOver(a, onA, b, onB);
                    else PlaceOver(b, onB, a, onA);
                }
            }
        }

        // Puts the rope over (or under) every rope it crosses, keeping its shape on the board.
        public static void Restack(StageJson stage, RopeJson rope, bool onTop)
        {
            if (!HasPath(rope)) return;
            foreach (var other in stage.ropes)
            {
                if (other == rope || !HasPath(other)) continue;
                foreach (var (onRope, onOther) in RopeGeometry.Crossings(Xy(rope.path), Xy(other.path)))
                {
                    if (onTop) PlaceOver(rope, onRope, other, onOther);
                    else PlaceOver(other, onOther, rope, onRope);
                }
            }
        }

        public static List<PathPointJson> Arc(StageJson stage, RopeJson rope, float bend)
        {
            var a = (Vector2)stage.pins.Find(p => p.id == rope.pinA).Cell;
            var b = (Vector2)stage.pins.Find(p => p.id == rope.pinB).Cell;
            var chord = b - a;
            float c = chord.magnitude;
            var left = c > 1e-4f ? new Vector2(-chord.y, chord.x) / c : Vector2.zero;

            // Parabola arc length ~ c + 8s^2/(3c): solve the sag s for the slack a rest-length rope has.
            float slack = Mathf.Max(0f, MinPathLength - c);
            float sag = Mathf.Min(MaxBulge, Mathf.Sqrt(3f * c * slack / 8f));
            float bulge = Mathf.Max(Mathf.Abs(bend), sag) * (bend < 0f ? -1f : 1f);

            var xy = new Vector2[PointCount];
            for (int i = 0; i < PointCount; i++)
            {
                float u = i / (PointCount - 1f);
                var p = Vector2.Lerp(a, b, u) + left * (bulge * 4f * u * (1f - u));
                p.x = Mathf.Clamp(p.x, -BoardMargin, stage.gridWidth - 1 + BoardMargin);
                p.y = Mathf.Clamp(p.y, -BoardMargin, stage.gridHeight - 1 + BoardMargin);
                xy[i] = p;
            }
            xy[0] = a;
            xy[^1] = b;

            var s = ArcPositions(xy);
            var path = new List<PathPointJson>(PointCount);
            for (int i = 0; i < PointCount; i++)
                path.Add(new PathPointJson { x = xy[i].x, y = xy[i].y, h = BaseHeight(s, i) });
            return path;
        }

        public static float HeightAt(List<PathPointJson> path, float index)
        {
            int i = Mathf.Clamp((int)index, 0, path.Count - 2);
            return Mathf.Lerp(path[i].h, path[i + 1].h, index - i);
        }

        public static Vector2[] Xy(List<PathPointJson> path)
        {
            var pts = new Vector2[path.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(path[i].x, path[i].y);
            return pts;
        }

        private static bool HasPath(RopeJson rope) => rope.path is { Count: >= 2 };

        private static void PlaceOver(RopeJson over, float onOver, RopeJson under, float onUnder)
        {
            float hu = HeightAt(under.path, onUnder);
            if (hu >= HeightAt(over.path, onOver))
            {
                Bump(under.path, onUnder, float.NegativeInfinity);
                hu = HeightAt(under.path, onUnder);
            }
            Bump(over.path, onOver, hu + Lift);
        }

        // Raises the path to target around the crossing with a cosine falloff; -infinity drops it back to its base height.
        private static void Bump(List<PathPointJson> path, float at, float target)
        {
            var s = ArcPositions(Xy(path));
            int i0 = Mathf.Clamp((int)at, 0, path.Count - 2);
            float sAt = Mathf.Lerp(s[i0], s[i0 + 1], at - i0);
            for (int i = 1; i < path.Count - 1; i++)
            {
                float d = Mathf.Abs(s[i] - sAt);
                if (d >= BumpRadius) continue;
                float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * d / BumpRadius));
                var p = path[i];
                float baseH = BaseHeight(s, i);
                p.h = float.IsNegativeInfinity(target)
                    ? Mathf.Lerp(p.h, baseH, w)
                    : Mathf.Max(p.h, Mathf.Lerp(baseH, target, w));
                path[i] = p;
            }
        }

        private static float BaseHeight(float[] s, int i)
        {
            if (i == 0 || i == s.Length - 1) return PinHeight;
            float toPin = Mathf.Min(s[i], s[^1] - s[i]);
            return BoardHeight + NearPinLift * Mathf.Max(0f, 1f - toPin / NearPinRange);
        }

        private static float[] ArcPositions(Vector2[] xy)
        {
            var s = new float[xy.Length];
            for (int i = 1; i < xy.Length; i++) s[i] = s[i - 1] + Vector2.Distance(xy[i - 1], xy[i]);
            return s;
        }
    }
}
