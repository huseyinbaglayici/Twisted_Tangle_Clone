using System.Collections.Generic;
using TwistedTangle.Editor.Solver;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelCheckResult
    {
        public int LevelId;
        public bool Solvable;
        public int Moves;           // -1 = not solvable / not searched
        public int Crossings;       // raw crossing count at start
        public bool HitLimit;       // true = inconclusive (search cap hit)
        public int ExpandedNodes;
        public int ValidationErrors;
    }

    /// <summary>
    /// Scans every level in the configured Levels folder and reports solvability in one pass.
    /// Called by <see cref="AdvancedToolsWindow"/>; not exposed as a standalone menu item.
    /// </summary>
    public static class LevelBatchChecker
    {
        /// <summary>Checks every level asset in <see cref="LevelEditorPaths.Levels"/> and returns results sorted by id.</summary>
        public static IReadOnlyList<LevelCheckResult> CheckAll()
        {
            var entityLookup = LoadEntityLookup(out var knownTypeIds);

            var results = new List<LevelCheckResult>();
            var guids = AssetDatabase.FindAssets(
                $"t:{nameof(LevelDataSO)}", new[] { LevelEditorPaths.Levels });

            foreach (var guid in guids)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDataSO>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (level == null) continue;
                results.Add(Check(level, entityLookup, knownTypeIds));
            }

            results.Sort((a, b) => a.LevelId.CompareTo(b.LevelId));
            return results;
        }

        private static LevelCheckResult Check(
            LevelDataSO level,
            Dictionary<string, EntityDefinitionSO> entityLookup,
            HashSet<string> knownTypeIds)
        {
            var validation = LevelValidator.Validate(level, knownTypeIds);

            var locked = new HashSet<Vector2Int>();
            foreach (var entity in level.GridEntities)
                if (entityLookup.TryGetValue(entity.TypeId, out var def) && IsNailed(def))
                    locked.Add(entity.Coordinates);

            var solve = LevelSolver.Solve(level, new SolveOptions
            {
                LockedCells = locked,
                MaxRopeReach = 3,
                CrossingOverrides = new HashSet<CrossingOverride>(level.CrossingOverrides)
            });

            return new LevelCheckResult
            {
                LevelId          = level.LevelId,
                Solvable         = solve.Solvable,
                Moves            = solve.Moves,
                Crossings        = solve.InitialCrossings,
                HitLimit         = solve.HitExpansionLimit,
                ExpandedNodes    = solve.ExpandedNodes,
                ValidationErrors = validation.Errors.Count
            };
        }

        private static Dictionary<string, EntityDefinitionSO> LoadEntityLookup(out HashSet<string> knownTypeIds)
        {
            knownTypeIds = new HashSet<string>();
            var lookup = new Dictionary<string, EntityDefinitionSO>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityDefinitionSO)}"))
            {
                var def = AssetDatabase.LoadAssetAtPath<EntityDefinitionSO>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                knownTypeIds.Add(def.TypeId);
                lookup[def.TypeId] = def;
            }
            return lookup;
        }

        private static bool IsNailed(EntityDefinitionSO def)
        {
            foreach (var tag in def.Tags)
                if (string.Equals(tag, "nailed", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "locked", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
