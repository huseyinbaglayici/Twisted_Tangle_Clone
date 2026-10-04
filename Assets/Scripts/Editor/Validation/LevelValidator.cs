using System.Collections.Generic;
using Runtime.Level;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelMetrics
    {
        public int PinCount;
        public int LockedPinCount;
        public int RopeCount;
        public int CrossingPairs;   // rope pairs that touch (touching ropes can't merge)
        public int TangledRopes;    // ropes touching at least one other rope
        public int FreeCells;
    }

    /// <summary>Outcome of validating a level: blocking errors, advisory warnings, and metrics.</summary>
    public class ValidationReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public LevelMetrics Metrics;

        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Catches broken levels in the editor using the same rules the runtime plays by (BoardModel /
    /// RopeGeometry / RopeTensionRule), so a green level here loads and behaves the same in game.
    /// </summary>
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
            if (level.moveLimit < 0 || level.timeLimitSeconds < 0)
                report.Errors.Add("Move limit / time limit can't be negative (0 = unlimited).");

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

            // --- pins ---------------------------------------------------------------------------
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

            // --- ropes --------------------------------------------------------------------------
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
                float max = RopeTensionRule.MaxLength(RopeTensionRule.RestLength);
                float warn = RopeTensionRule.RestLength * (1f + RopeTensionRule.WarnPercentage * RopeTensionRule.MaxTension);
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

            // --- tutorial -----------------------------------------------------------------------
            if (level.tutorial != null && level.tutorial.pinId >= 0)
            {
                if (!pinsById.ContainsKey(level.tutorial.pinId))
                    report.Errors.Add($"Tutorial pin {level.tutorial.pinId} doesn't exist.");
                if (!InBounds(level.tutorial.TargetCell, stage) || occupied.Contains(level.tutorial.TargetCell))
                    report.Errors.Add($"Tutorial target {level.tutorial.TargetCell} must be an empty cell.");
            }

            // --- metrics (runtime crossing rule) ------------------------------------------------
            var metrics = new LevelMetrics
            {
                PinCount = stage.pins.Count,
                RopeCount = stage.ropes.Count,
                FreeCells = stage.gridWidth * stage.gridHeight - occupied.Count,
            };
            foreach (var pin in stage.pins) if (pin.locked) metrics.LockedPinCount++;

            if (ropesValid && report.Errors.Count == 0)
            {
                var board = BuildBoard(level);
                int sum = 0;
                foreach (var r in board.Ropes)
                {
                    sum += r.CrossingCount;
                    if (r.CrossingCount > 0) metrics.TangledRopes++;
                }
                metrics.CrossingPairs = sum / 2;

                if (stage.ropes.Count > 0 && metrics.TangledRopes == 0)
                    report.Warnings.Add("No rope touches another — the level is already solved.");
            }

            report.Metrics = metrics;
            return report;
        }

        /// <summary>Loads stage 1 into the runtime BoardModel (call only on a level without errors).</summary>
        public static BoardModel BuildBoard(LevelJson level)
        {
            var stage = level.stages[0];
            var board = new BoardModel();
            board.Load(stage, level.moveLimit, new BoardGrid(stage.gridWidth, stage.gridHeight));
            return board;
        }

        private static bool InBounds(Vector2Int c, StageJson stage) =>
            c.x >= 0 && c.y >= 0 && c.x < stage.gridWidth && c.y < stage.gridHeight;
    }
}
