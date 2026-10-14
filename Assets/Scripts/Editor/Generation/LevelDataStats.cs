using System.Collections.Generic;
using System.Linq;
using Runtime.Level;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Editor.Validation;

namespace TwistedTangle.Editor.Generation
{
    public readonly struct StatRange
    {
        public readonly float Min, Max, Median;

        public StatRange(IEnumerable<float> values)
        {
            var v = values.OrderBy(x => x).ToList();
            Min = v.Count > 0 ? v[0] : 0f;
            Max = v.Count > 0 ? v[^1] : 0f;
            Median = v.Count > 0 ? v[v.Count / 2] : 0f;
        }

        public string Ints => Min == Max ? $"{Min:0}" : $"{Min:0}–{Max:0}";
        public string Cells => $"{Min:0.0}–{Max:0.0}";
    }

    // Difficulty profile measured from the shipped levels of one level range.
    public sealed class DifficultyProfile
    {
        public string Name;
        public int FromLevel, ToLevel;
        public List<LevelJson> Levels = new();
        public List<int> MapIds = new();
        public StatRange Ropes, Pins, SharedPins, LockedPins, TimeLimit, RopeLength, ContactPairs, Hooks, Moves;
        public int LevelsWithLocked, LevelsWithShared;

        public string Summary =>
            $"Levels {FromLevel}–{ToLevel}: {Ropes.Ints} ropes, {ContactPairs.Ints} contacts, {Hooks.Ints} hooks, " +
            $"~{Moves.Ints} moves, {TimeLimit.Ints} s";
    }

    public static class LevelDataStats
    {
        public static readonly (string name, int from, int to)[] Buckets =
        {
            ("Easy", 1, 10), ("Medium", 11, 20), ("Hard", 21, 30),
        };

        public static string BucketOf(int levelNumber) =>
            Buckets.FirstOrDefault(b => levelNumber >= b.from && levelNumber <= b.to).name ?? Buckets[^1].name;

        public static DifficultyProfile Profile(string difficulty)
        {
            var bucket = Buckets.FirstOrDefault(b => b.name == difficulty);
            if (bucket.name == null) bucket = Buckets[0];
            var levels = LevelFileUtility.LoadAll()
                .Where(l => l.levelNumber >= bucket.from && l.levelNumber <= bucket.to && l.stages.Count > 0)
                .ToList();
            return Measure(bucket.name, bucket.from, bucket.to, levels);
        }

        private static DifficultyProfile Measure(string name, int from, int to, List<LevelJson> levels)
        {
            var reports = levels.Select(LevelValidator.Validate).ToList();
            var stages = levels.Select(l => l.stages[0]).ToList();
            var lengths = new List<float>();
            foreach (var s in stages)
            foreach (var r in s.ropes)
            {
                var a = s.pins.Find(p => p.id == r.pinA);
                var b = s.pins.Find(p => p.id == r.pinB);
                if (a != null && b != null) lengths.Add(UnityEngine.Vector2.Distance(a.Cell, b.Cell));
            }

            return new DifficultyProfile
            {
                Name = name,
                FromLevel = from,
                ToLevel = to,
                Levels = levels,
                MapIds = stages.Select(s => s.mapId).Distinct().OrderBy(m => m).ToList(),
                Ropes = new StatRange(reports.Select(r => (float)r.Metrics.RopeCount)),
                Pins = new StatRange(reports.Select(r => (float)r.Metrics.PinCount)),
                SharedPins = new StatRange(reports.Select(r => (float)r.Metrics.SharedPinCount)),
                LockedPins = new StatRange(reports.Select(r => (float)r.Metrics.LockedPinCount)),
                TimeLimit = new StatRange(levels.Select(l => (float)l.timeLimitSeconds)),
                RopeLength = new StatRange(lengths),
                ContactPairs = new StatRange(reports.Select(r => (float)r.Metrics.CrossingPairs)),
                Hooks = new StatRange(reports.Select(r => (float)r.Metrics.Hooks)),
                Moves = new StatRange(reports.Select(r => (float)r.Metrics.EstimatedMoves)),
                LevelsWithLocked = reports.Count(r => r.Metrics.LockedPinCount > 0),
                LevelsWithShared = reports.Count(r => r.Metrics.SharedPinCount > 0),
            };
        }
    }
}
