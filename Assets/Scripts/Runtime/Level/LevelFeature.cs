using System;
using System.Collections.Generic;

namespace Runtime.Level
{
    // Mechanics on top of plain pins and ropes. The first level that uses one explains it with an intro popup.
    public enum LevelFeature
    {
        SharedPin,
        Funnel,
        LockedPin,
        BlockedCell
    }

    public static class LevelFeatures
    {
        private static readonly LevelFeature[] All = (LevelFeature[])Enum.GetValues(typeof(LevelFeature));

        public static List<LevelFeature> Of(LevelJson level)
        {
            var used = new List<LevelFeature>();
            foreach (var feature in All)
                if (level.stages.Exists(stage => Uses(stage, feature))) used.Add(feature);
            return used;
        }

        private static bool Uses(StageJson stage, LevelFeature feature) => feature switch
        {
            LevelFeature.SharedPin => HasSharedPin(stage),
            LevelFeature.Funnel => stage.funnelCells is { Count: > 0 },
            LevelFeature.LockedPin => stage.pins.Exists(p => p.locked),
            LevelFeature.BlockedCell => stage.blockedCells is { Count: > 0 },
            _ => false
        };

        private static bool HasSharedPin(StageJson stage)
        {
            var seen = new HashSet<int>();
            foreach (var rope in stage.ropes)
                if (!seen.Add(rope.pinA) || !seen.Add(rope.pinB)) return true;
            return false;
        }
    }
}
