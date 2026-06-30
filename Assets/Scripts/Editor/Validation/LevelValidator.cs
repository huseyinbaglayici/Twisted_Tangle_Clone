using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Editor.Settings;
using TwistedTangle.Runtime.Data.Enums;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Editor.Geometry;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelMetrics
    {
        public int EntityCount;
        public int RopeCount;
        public int CrossingCount;
        public int TangleResidual;
        public bool Separable;
        public int ColorCount;
        public int OverrideCount;
        public float TotalPathLength;
        public float DifficultyScore;
        public LevelDifficulty Difficulty;
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
        public static ValidationReport Validate(LevelDataSO level, ICollection<string> knownEntityTypeIds,
            DifficultySettingsSO settings = null)
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
            foreach (var entity in level.GridEntities)
            {
                if (!entityCells.Add(entity.Coordinates))
                    report.Errors.Add($"Duplicate entity at {entity.Coordinates}.");

                if (!InBounds(entity.Coordinates, level))
                    report.Errors.Add($"Entity at {entity.Coordinates} is outside the grid.");

                if (knownEntityTypeIds != null && !knownEntityTypeIds.Contains(entity.TypeId))
                    report.Errors.Add($"Entity at {entity.Coordinates} has unknown type '{entity.TypeId}'.");
            }

            // Rope checks + which entities actually get used.
            // NOTE: rope PegCoords are sub-grid coordinates; entity Coordinates are coarse.
            // Use CrossingSolver.SubToPinCoord to convert endpoints for entity lookup.
            var usedEntities = new HashSet<Vector2Int>(); // coarse coords
            int subMax = CrossingSolver.SubDiv;
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
                    var subCoord = wp.PegCoord;

                    if (!wp.IsBendPoint)
                    {
                        // Pin waypoint: sub-grid → coarse for entity lookup.
                        var coarseCoord = CrossingSolver.SubToPinCoord(subCoord);
                        usedEntities.Add(coarseCoord);
                        if (!entityCells.Contains(coarseCoord))
                        {
                            string where = i == 0 || i == rope.Path.Count - 1 ? "endpoint" : "waypoint";
                            report.Errors.Add($"Rope {rope.RopeId} {where} at {coarseCoord} is not on an entity.");
                        }
                    }
                    else
                    {
                        // Bend point: validate sub-grid bounds.
                        if (subCoord.x < 0 || subCoord.y < 0 ||
                            subCoord.x >= level.GridWidth * subMax || subCoord.y >= level.GridHeight * subMax)
                            report.Errors.Add($"Rope {rope.RopeId} bend at sub-grid {subCoord} is outside the grid.");
                    }

                    if (i > 0 && rope.Path[i - 1].PegCoord == subCoord)
                        report.Warnings.Add($"Rope {rope.RopeId} repeats the same position {subCoord}.");
                }
            }

            // --- warnings ----------------------------------------------------------------------
            foreach (var entity in level.GridEntities)
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
            report.Metrics = BuildMetrics(level, crossings.Count, settings);
            report.Metrics.TangleResidual = residual;
            report.Metrics.Separable = residual == 0;
            return report;
        }

        private static LevelMetrics BuildMetrics(LevelDataSO level, int crossingCount, DifficultySettingsSO settings)
        {
            float length = 0f;
            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null) continue;
                for (int i = 1; i < rope.Path.Count; i++)
                    length += Vector2.Distance(
                        CrossingSolver.SubCenter(rope.Path[i - 1].PegCoord),
                        CrossingSolver.SubCenter(rope.Path[i].PegCoord));
            }

            int colorCount = level.Ropes
                .Where(r => r is { Path: { Count: >= 2 } })
                .Select(r => r.Tint)
                .Distinct()
                .Count();

            settings ??= DifficultySettingsSO.LoadOrCreate();
            float score = settings.ComputeScore(crossingCount, level.Ropes.Count, colorCount, length, level.CrossingOverrides.Count);

            return new LevelMetrics
            {
                EntityCount     = level.GridEntities.Count,
                RopeCount       = level.Ropes.Count,
                CrossingCount   = crossingCount,
                ColorCount      = colorCount,
                OverrideCount   = level.CrossingOverrides.Count,
                TotalPathLength = length,
                DifficultyScore = score,
                Difficulty      = settings.Classify(score),
            };
        }

        private static bool InBounds(Vector2Int c, LevelDataSO level) =>
            c.x >= 0 && c.y >= 0 && c.x < level.GridWidth && c.y < level.GridHeight;
    }
}
