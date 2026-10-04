using System.Collections.Generic;
using TwistedTangle.Editor.Utils;

namespace TwistedTangle.Editor.Validation
{
    public struct LevelCheckResult
    {
        public int LevelId;
        public int Crossings;
        public int ValidationErrors;
    }

    /// <summary>
    /// Scans every level JSON in the configured Levels folder and reports crossing/validation status in one pass.
    /// Called by <see cref="AdvancedToolsWindow"/>; not exposed as a standalone menu item.
    /// </summary>
    public static class LevelBatchChecker
    {
        public static IReadOnlyList<LevelCheckResult> CheckAll()
        {
            var results = new List<LevelCheckResult>();
            foreach (int n in LevelFileUtility.ListLevelNumbers())
            {
                var level = LevelFileUtility.Load(n);
                if (level == null) continue;
                var report = LevelValidator.Validate(level);
                results.Add(new LevelCheckResult
                {
                    LevelId = n,
                    Crossings = report.Metrics.CrossingPairs,
                    ValidationErrors = report.Errors.Count
                });
            }
            return results;
        }
    }
}
