using System.Collections.Generic;
using System.Linq;
using Editor.Geometry;
using Runtime.Level;
using TwistedTangle.Editor.Utils;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelMetrics
    {
        public int PinCount;
        public int LockedPinCount;
        public int SharedPinCount;
        public int RopeCount;
        public int CrossingPairs;
        public int TangledRopes;
        public int Hooks;
        public int EstimatedMoves;
        public int FreeCells;
    }

    public class ValidationReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public LevelMetrics Metrics;

        public bool IsValid => Errors.Count == 0;
    }

    public enum PairKind { Touch, Over, Hook }

    // Two ropes in contact at the start. Tops lists the rope on top at each crossing, in order along A.
    public sealed class RopePair
    {
        public RopeJson A, B;
        public List<int> Tops = new();

        public PairKind Kind => Tops.Count == 0 ? PairKind.Touch : Tops.Distinct().Count() > 1 ? PairKind.Hook : PairKind.Over;
        public int TopId => Tops.Count > 0 ? Tops[0] : -1;
    }

    public static class LevelValidator
    {
        private const float EndpointTolerance = 0.1f;

        public static ValidationReport Validate(LevelJson level)
        {
            var report = new ValidationReport();
            if (level == null || level.stages == null || level.stages.Count == 0)
            {
                report.Errors.Add("No level data.");
                return report;
            }

            if (level.levelNumber < 1)
                report.Errors.Add("Level number must be >= 1.");
            if (level.timeLimitSeconds <= 0)
                report.Errors.Add("Time limit must be > 0 seconds.");

            var stage = level.stages[0];
            if (level.stages.Count > 1)
                report.Warnings.Add($"Level has {level.stages.Count} stages; the editor shows stage 1 only.");

            if (stage.gridWidth <= 0 || stage.gridHeight <= 0)
            {
                report.Errors.Add($"Grid size is invalid ({stage.gridWidth}x{stage.gridHeight}).");
                return report;
            }

            if (stage.gridWidth != StageJson.WidthForMap(stage.mapId) ||
                stage.gridHeight != StageJson.HeightForMap(stage.mapId))
                report.Warnings.Add($"Grid {stage.gridWidth}x{stage.gridHeight} doesn't match map {stage.mapId} " +
                                    $"({StageJson.WidthForMap(stage.mapId)}x{StageJson.HeightForMap(stage.mapId)}).");

            var pinsById = new Dictionary<int, PinJson>();
            var occupied = new HashSet<Vector2Int>();
            foreach (var pin in stage.pins)
            {
                if (!pinsById.TryAdd(pin.id, pin))
                    report.Errors.Add($"Duplicate pin id {pin.id}.");
                if (!InBounds(pin.Cell, stage))
                    report.Errors.Add($"Pin {pin.id} at {pin.Cell} is outside the grid.");
                if (!occupied.Add(pin.Cell))
                    report.Errors.Add($"Two pins share cell {pin.Cell}.");
            }

            var palette = LevelFileUtility.FindPalette();
            int colorCount = palette != null ? palette.Count : 0;
            float max = TensionLimits.MaxLength(TensionLimits.RestLength);
            float warn = TensionLimits.RestLength * (1f + TensionLimits.WarnPercentage * TensionLimits.MaxTension);

            var ropeIds = new HashSet<int>();
            var usedPins = new Dictionary<int, int>();
            bool ropesValid = true;
            foreach (var rope in stage.ropes)
            {
                if (!ropeIds.Add(rope.id))
                    report.Errors.Add($"Duplicate rope id {rope.id}.");
                if (colorCount > 0 && (rope.colorIndex < 0 || rope.colorIndex >= colorCount))
                    report.Errors.Add($"Rope {rope.id} color {rope.colorIndex} is outside the palette (0..{colorCount - 1}).");

                if (!pinsById.TryGetValue(rope.pinA, out var a) || !pinsById.TryGetValue(rope.pinB, out var b))
                {
                    report.Errors.Add($"Rope {rope.id} references a missing pin ({rope.pinA} → {rope.pinB}).");
                    ropesValid = false;
                    continue;
                }

                if (rope.pinA == rope.pinB)
                {
                    report.Errors.Add($"Rope {rope.id} starts and ends on the same pin.");
                    ropesValid = false;
                    continue;
                }

                usedPins[rope.pinA] = usedPins.GetValueOrDefault(rope.pinA) + 1;
                usedPins[rope.pinB] = usedPins.GetValueOrDefault(rope.pinB) + 1;

                float length = Vector2.Distance(a.Cell, b.Cell);
                if (length > max)
                    report.Errors.Add($"Rope {rope.id} is too long ({length:0.00} > {max:0.00}) — over max tension.");
                else if (length > warn)
                    report.Warnings.Add($"Rope {rope.id} starts red/tense ({length:0.00} > {warn:0.00}).");

                if (rope.path == null || rope.path.Count != RopePathBuilder.PointCount)
                {
                    report.Errors.Add($"Rope {rope.id} needs a {RopePathBuilder.PointCount}-point path (has {rope.path?.Count ?? 0}).");
                    ropesValid = false;
                }
                else if (Vector2.Distance(Xy(rope.path[0]), a.Cell) > EndpointTolerance ||
                         Vector2.Distance(Xy(rope.path[^1]), b.Cell) > EndpointTolerance)
                {
                    report.Errors.Add($"Rope {rope.id} path doesn't start at pin {rope.pinA} and end at pin {rope.pinB}.");
                    ropesValid = false;
                }
            }

            foreach (var pin in stage.pins)
                if (!usedPins.ContainsKey(pin.id))
                    report.Warnings.Add($"Pin {pin.id} at {pin.Cell} is not used by any rope.");

            if (level.tutorial != null && level.tutorial.pinId >= 0)
            {
                if (!pinsById.ContainsKey(level.tutorial.pinId))
                    report.Errors.Add($"Tutorial pin {level.tutorial.pinId} doesn't exist.");
                if (!InBounds(level.tutorial.TargetCell, stage) || occupied.Contains(level.tutorial.TargetCell))
                    report.Errors.Add($"Tutorial target {level.tutorial.TargetCell} must be an empty cell.");
            }

            var metrics = new LevelMetrics
            {
                PinCount = stage.pins.Count,
                RopeCount = stage.ropes.Count,
                SharedPinCount = usedPins.Count(kv => kv.Value > 1),
                LockedPinCount = stage.pins.Count(p => p.locked),
                FreeCells = stage.gridWidth * stage.gridHeight - occupied.Count,
            };

            if (ropesValid && report.Errors.Count == 0)
            {
                var pairs = AnalyzePairs(stage);
                var touching = new HashSet<int>();
                foreach (var p in pairs) { touching.Add(p.A.id); touching.Add(p.B.id); }
                metrics.CrossingPairs = pairs.Count;
                metrics.TangledRopes = touching.Count;
                metrics.Hooks = pairs.Count(p => p.Kind == PairKind.Hook);
                metrics.EstimatedMoves = EstimateMoves(pairs);

                var free = stage.ropes.Where(r => !touching.Contains(r.id)).Select(r => r.id).ToList();
                if (free.Count > 0)
                    report.Errors.Add($"Rope(s) {string.Join(", ", free)} touch no other rope at the start (free immediately).");

                var stuck = RopesThatCanNeverMerge(stage);
                if (stuck.Count > 0)
                    report.Errors.Add($"Rope(s) {string.Join(", ", stuck)} can never merge: every pin they use keeps another rope.");
            }

            report.Metrics = metrics;
            return report;
        }

        public static List<RopePair> AnalyzePairs(StageJson stage)
        {
            var pairs = new List<RopePair>();
            var xy = stage.ropes.Select(r => RopePathBuilder.Xy(r.path)).ToList();
            for (int i = 0; i < stage.ropes.Count; i++)
            for (int j = i + 1; j < stage.ropes.Count; j++)
            {
                if (!RopeGeometry.PathsTouch(xy[i], xy[j])) continue;
                RopeJson a = stage.ropes[i], b = stage.ropes[j];
                var pair = new RopePair { A = a, B = b };
                foreach (var (onA, onB) in RopeGeometry.Crossings(xy[i], xy[j]))
                    pair.Tops.Add(RopePathBuilder.HeightAt(a.path, onA) >= RopePathBuilder.HeightAt(b.path, onB) ? a.id : b.id);
                pairs.Add(pair);
            }
            return pairs;
        }

        // Moving the rope underneath drags the top rope along, so a pair with a clear top needs the top rope moved,
        // a hooked pair needs both ropes moved and a plain touch needs either. One move clears all pairs of a rope.
        public static int EstimateMoves(List<RopePair> pairs)
        {
            var moved = new HashSet<int>();
            foreach (var p in pairs)
            {
                if (p.Kind == PairKind.Over) moved.Add(p.TopId);
                else if (p.Kind == PairKind.Hook) { moved.Add(p.A.id); moved.Add(p.B.id); }
            }
            foreach (var p in pairs)
                if (p.Kind == PairKind.Touch && !moved.Contains(p.A.id) && !moved.Contains(p.B.id))
                    moved.Add(p.A.id);
            return moved.Count;
        }

        // A rope merges only if one of its pins holds no other remaining rope; peel ropes off until nothing changes.
        private static List<int> RopesThatCanNeverMerge(StageJson stage)
        {
            var left = new List<RopeJson>(stage.ropes);
            bool changed = true;
            while (changed && left.Count > 0)
            {
                changed = false;
                for (int i = left.Count - 1; i >= 0; i--)
                {
                    var r = left[i];
                    bool aAlone = left.Count(o => o.pinA == r.pinA || o.pinB == r.pinA) == 1;
                    bool bAlone = left.Count(o => o.pinA == r.pinB || o.pinB == r.pinB) == 1;
                    if (!aAlone && !bAlone) continue;
                    left.RemoveAt(i);
                    changed = true;
                }
            }
            return left.Select(r => r.id).ToList();
        }

        public static HashSet<int> TangledRopeIds(LevelJson level, out int crossingPairs)
        {
            var stage = level.stages[0];
            var paths = stage.ropes.Select(r => PathOf(stage, r)).ToList();

            var tangled = new HashSet<int>();
            crossingPairs = 0;
            for (int i = 0; i < paths.Count; i++)
            for (int j = i + 1; j < paths.Count; j++)
            {
                if (!RopeGeometry.PathsTouch(paths[i], paths[j])) continue;
                crossingPairs++;
                tangled.Add(stage.ropes[i].id);
                tangled.Add(stage.ropes[j].id);
            }
            return tangled;
        }

        private static Vector2[] PathOf(StageJson stage, RopeJson rope)
        {
            var a = stage.pins.Find(p => p.id == rope.pinA).Cell;
            var b = stage.pins.Find(p => p.id == rope.pinB).Cell;
            if (rope.path == null || rope.path.Count < 2) return RopeShape.Straight(a, b);

            var pts = RopePathBuilder.Xy(rope.path);
            pts[0] = a;
            pts[^1] = b;
            return pts;
        }

        private static Vector2 Xy(PathPointJson p) => new(p.x, p.y);

        private static bool InBounds(Vector2Int c, StageJson stage) =>
            c.x >= 0 && c.y >= 0 && c.x < stage.gridWidth && c.y < stage.gridHeight;
    }
}
