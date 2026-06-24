using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Editor.Geometry;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public enum DifficultyBucket
    {
        Easy,
        Medium,
        Hard
    }

    /// <summary>Read-only metrics summarizing a level, shown to the designer for self-service.</summary>
    public struct LevelMetrics
    {
        public int EntityCount;
        public int RopeCount;
        public int CrossingCount;
        public int TangleResidual;   // crossings left after peeling top ropes (0 = separable)
        public bool Separable;       // true => the tangle can be lifted apart (TangleResidual == 0)
        public int ColorCount;
        public int OverrideCount;
        public float TotalPathLength;
        public float DifficultyScore;
        public DifficultyBucket Difficulty;
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
    /// Catches broken/incomplete levels in the editor (not in game) and reports rough difficulty so
    /// designers can fix things themselves instead of bouncing every issue back to an engineer.
    /// </summary>
    public static class LevelValidator
    {
        // Difficulty weights — tuned heuristically; crossings and color variety drive most of it.
        private const float WCrossings = 1.0f;
        private const float WRopes = 0.5f;
        private const float WColors = 1.5f;
        private const float WLength = 0.2f;
        private const float WOverrides = 1.0f;
        private const float MediumThreshold = 10f;
        private const float HardThreshold = 24f;

        public static ValidationReport Validate(LevelDataSO level, ICollection<string> knownEntityTypeIds)
        {
            var report = new ValidationReport();
            if (level == null)
            {
                report.Errors.Add("No level data.");
                return report;
            }

            // --- structural errors -------------------------------------------------------------
            if (level.LevelId < 1)
                report.Errors.Add("Level Id must be >= 1 (0 is reserved for \"no level\").");
            if (level.GridWidth <= 0 || level.GridHeight <= 0)
                report.Errors.Add($"Grid size is invalid ({level.GridWidth}x{level.GridHeight}).");
            if (level.TimeSeconds <= 0)
                report.Errors.Add("Level time must be greater than 0 seconds.");

            // Entity coordinate index + duplicate / unknown-type / out-of-bounds checks.
            var entityCells = new HashSet<Vector2Int>();
            foreach (var entity in level.Pegs)
            {
                if (!entityCells.Add(entity.Coordinates))
                    report.Errors.Add($"Duplicate entity at {entity.Coordinates}.");

                if (!InBounds(entity.Coordinates, level))
                    report.Errors.Add($"Entity at {entity.Coordinates} is outside the grid.");

                if (knownEntityTypeIds != null && !knownEntityTypeIds.Contains(entity.TypeId))
                    report.Errors.Add($"Entity at {entity.Coordinates} has unknown type '{entity.TypeId}'.");
            }

            // Rope checks + which entities actually get used.
            var usedEntities = new HashSet<Vector2Int>();
            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2)
                {
                    report.Errors.Add($"Rope {rope?.RopeId} has fewer than 2 waypoints.");
                    continue;
                }

                for (int i = 0; i < rope.Path.Count; i++)
                {
                    var wp = rope.Path[i];
                    var coord = wp.PegCoord;

                    if (!wp.IsBendPoint)
                    {
                        usedEntities.Add(coord);
                        if (!entityCells.Contains(coord))
                        {
                            string where = i == 0 || i == rope.Path.Count - 1 ? "endpoint" : "waypoint";
                            report.Errors.Add($"Rope {rope.RopeId} {where} at {coord} is not on an entity.");
                        }
                    }

                    if (i > 0 && rope.Path[i - 1].PegCoord == coord)
                        report.Warnings.Add($"Rope {rope.RopeId} repeats the same cell {coord}.");
                }
            }

            // --- warnings ----------------------------------------------------------------------
            foreach (var entity in level.Pegs)
                if (entityCells.Contains(entity.Coordinates) && !usedEntities.Contains(entity.Coordinates))
                    report.Warnings.Add($"Entity at {entity.Coordinates} is not used by any rope.");

            var crossings = CrossingSolver.FindCrossings(level.Ropes);
            var ropesWithCrossing = new HashSet<int>();
            foreach (var c in crossings)
            {
                ropesWithCrossing.Add(c.RopeIdA);
                ropesWithCrossing.Add(c.RopeIdB);
            }

            foreach (var rope in level.Ropes)
                if (rope is { Path: { Count: >= 2 } } && !ropesWithCrossing.Contains(rope.RopeId))
                    report.Warnings.Add($"Rope {rope.RopeId} never crosses another rope (trivial).");

            // Peelability: how tangled the level really is once over/under is taken into account.
            var aOver = CrossingSolver.ResolveOverUnder(level.Ropes, crossings, level.CrossingOverrides);
            int residual = CrossingSolver.PeelResidual(level.Ropes, crossings, aOver);

            // --- metrics + difficulty ----------------------------------------------------------
            report.Metrics = BuildMetrics(level, crossings.Count);
            report.Metrics.TangleResidual = residual;
            report.Metrics.Separable = residual == 0;
            return report;
        }

        private static LevelMetrics BuildMetrics(LevelDataSO level, int crossingCount)
        {
            float length = 0f;
            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null) continue;
                for (int i = 1; i < rope.Path.Count; i++)
                    length += Vector2.Distance(
                        CrossingSolver.Center(rope.Path[i - 1].PegCoord),
                        CrossingSolver.Center(rope.Path[i].PegCoord));
            }

            int colorCount = level.Ropes
                .Where(r => r is { Path: { Count: >= 2 } })
                .Select(r => r.Tint)
                .Distinct()
                .Count();

            float score = WCrossings * crossingCount
                          + WRopes * level.Ropes.Count
                          + WColors * colorCount
                          + WLength * length
                          + WOverrides * level.CrossingOverrides.Count;

            return new LevelMetrics
            {
                EntityCount = level.Pegs.Count,
                RopeCount = level.Ropes.Count,
                CrossingCount = crossingCount,
                ColorCount = colorCount,
                OverrideCount = level.CrossingOverrides.Count,
                TotalPathLength = length,
                DifficultyScore = score,
                Difficulty = score < MediumThreshold ? DifficultyBucket.Easy
                    : score < HardThreshold ? DifficultyBucket.Medium
                    : DifficultyBucket.Hard
            };
        }

        private static bool InBounds(Vector2Int c, LevelDataSO level) =>
            c.x >= 0 && c.y >= 0 && c.x < level.GridWidth && c.y < level.GridHeight;
    }
}
