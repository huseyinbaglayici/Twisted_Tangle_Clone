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
