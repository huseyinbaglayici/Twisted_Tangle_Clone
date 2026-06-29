using System.Collections.Generic;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelCheckResult
    {
        public int LevelId;
        public int Crossings;
        public int ValidationErrors;
    }

    /// <summary>
    /// Scans every level in the configured Levels folder and reports crossing/validation status in one pass.
    /// Called by <see cref="AdvancedToolsWindow"/>; not exposed as a standalone menu item.
    /// </summary>
    public static class LevelBatchChecker
    {
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
                results.Add(Check(level, knownTypeIds));
            }

            results.Sort((a, b) => a.LevelId.CompareTo(b.LevelId));
            return results;
        }

        private static LevelCheckResult Check(LevelDataSO level, HashSet<string> knownTypeIds)
        {
            var validation = LevelValidator.Validate(level, knownTypeIds);
            var crossings  = TwistedTangle.Editor.Geometry.CrossingSolver.FindCrossings(level.Ropes);
            int inter = 0;
            foreach (var c in crossings) if (c.RopeIndexA != c.RopeIndexB) inter++;

            return new LevelCheckResult
            {
                LevelId          = level.LevelId,
                Crossings        = inter,
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
    }
}
