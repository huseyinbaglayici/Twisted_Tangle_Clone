using System.Collections.Generic;
using Editor.Geometry;
using Runtime.Level;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelMetrics
    {
        public int PinCount;
        public int LockedPinCount;
        public int RopeCount;
        public int CrossingPairs;
        public int TangledRopes;
        public int FreeCells;
    }

    public class ValidationReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public LevelMetrics Metrics;

        public bool IsValid => Errors.Count == 0;
    }

    public static class LevelValidator
    {
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
            if (level.timeLimitSeconds < 0)
                report.Errors.Add("Time limit can't be negative (0 = unlimited).");

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

            var ropeIds = new HashSet<int>();
            var usedPins = new HashSet<int>();
            bool ropesValid = true;
            foreach (var rope in stage.ropes)
            {
                if (!ropeIds.Add(rope.id))
                    report.Errors.Add($"Duplicate rope id {rope.id}.");

                bool hasA = pinsById.TryGetValue(rope.pinA, out var a);
                bool hasB = pinsById.TryGetValue(rope.pinB, out var b);
                if (!hasA || !hasB)
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

                usedPins.Add(rope.pinA);
                usedPins.Add(rope.pinB);

                float length = Vector2.Distance(a.Cell, b.Cell);
                float max = TensionLimits.MaxLength(TensionLimits.RestLength);
                float warn = TensionLimits.RestLength * (1f + TensionLimits.WarnPercentage * TensionLimits.MaxTension);
                if (length > max)
                    report.Errors.Add($"Rope {rope.id} is too long ({length:0.00} > {max:0.00}) — over max tension.");
                else if (length > warn)
                    report.Warnings.Add($"Rope {rope.id} starts red/tense ({length:0.00} > {warn:0.00}).");

                if (rope.path != null && rope.path.Count == 1)
                    report.Warnings.Add($"Rope {rope.id} has a 1-point path (ignored, drawn straight).");
            }

            foreach (var pin in stage.pins)
                if (!usedPins.Contains(pin.id))
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
                FreeCells = stage.gridWidth * stage.gridHeight - occupied.Count,
            };
            foreach (var pin in stage.pins) if (pin.locked) metrics.LockedPinCount++;

            if (ropesValid && report.Errors.Count == 0)
            {
                var tangled = TangledRopeIds(level, out int pairs);
                metrics.CrossingPairs = pairs;
                metrics.TangledRopes = tangled.Count;

                if (stage.ropes.Count > 0 && metrics.TangledRopes == 0)
                    report.Warnings.Add("No rope touches another — the level is already solved.");
            }

            report.Metrics = metrics;
            return report;
        }

        public static HashSet<int> TangledRopeIds(LevelJson level, out int crossingPairs)
        {
            var stage = level.stages[0];
            var paths = new List<Vector2[]>();
            foreach (var rope in stage.ropes) paths.Add(PathOf(stage, rope));

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

            var pts = new Vector2[rope.path.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(rope.path[i].x, rope.path[i].y);
            pts[0] = a;
            pts[^1] = b;
            return pts;
        }

        private static bool InBounds(Vector2Int c, StageJson stage) =>
            c.x >= 0 && c.y >= 0 && c.x < stage.gridWidth && c.y < stage.gridHeight;
    }
}
