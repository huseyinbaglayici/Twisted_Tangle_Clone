using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Editor.Geometry;
using Runtime.Level;
using TwistedTangle.Editor.Validation;
using UnityEngine;

namespace TwistedTangle.Editor.Generation
{
    public sealed class LevelGenerationRequest
    {
        public int MapId = 1;
        public int TimeLimitSeconds;
        public string Difficulty = "Easy";
        public int PaletteCount = 8;
        public List<LevelJson> Examples = new();
    }

    // What the model writes: no rope paths, those are built in code from pins, bend and over/under.
    [Serializable]
    public class AiLevelJson
    {
        public string difficulty;
        public int timeLimitSeconds;
        public int mapId = 1;
        public List<PinJson> pins = new();
        public List<AiRopeJson> ropes = new();
        public List<AiCrossingJson> crossings = new();
        public List<Vector2Int> blockedCells = new();
        public List<Vector2Int> funnelCells = new();
    }

    [Serializable]
    public class AiRopeJson
    {
        public int id, pinA, pinB, colorIndex, layer;
        public float bend;
    }

    [Serializable]
    public class AiCrossingJson
    {
        public int a, b;
        public List<int> onTop = new();
    }

    public static class LevelAiGenerator
    {
        private const float MinBend = 0.15f;

        public static List<LevelJson> PickExamples(string difficulty, int count, int seed)
        {
            var levels = LevelDataStats.Profile(difficulty).Levels;
            var random = new System.Random(seed);
            return levels.OrderBy(_ => random.Next()).Take(count).OrderBy(l => l.levelNumber).ToList();
        }

        public static string DescribeLevel(LevelJson level)
        {
            if (level?.stages == null || level.stages.Count == 0) return null;
            var stage = level.stages[0];
            var report = LevelValidator.Validate(level);
            var sb = new StringBuilder();
            sb.Append($"Level {level.levelNumber}: map {stage.mapId}, {stage.pins.Count} pins, {stage.ropes.Count} ropes, " +
                      $"{level.timeLimitSeconds} s, ~{report.Metrics.EstimatedMoves} moves. Contacts: ");
            if (report.IsValid)
            {
                var parts = LevelValidator.AnalyzePairs(stage).Select(p => p.Kind switch
                {
                    PairKind.Hook => $"{p.A.id}/{p.B.id} hooked",
                    PairKind.Over => $"{p.TopId} over {(p.TopId == p.A.id ? p.B.id : p.A.id)}",
                    _ => $"{p.A.id}/{p.B.id} touch",
                });
                sb.Append(string.Join(", ", parts));
            }
            var ai = ToAiJson(level);
            ai.difficulty = LevelDataStats.BucketOf(level.levelNumber);
            sb.Append('\n').Append(ShortFloats(JsonUtility.ToJson(ai)));
            return sb.ToString();
        }

        public static string BuildManualPrompt(LevelGenerationRequest r)
        {
            var profile = LevelDataStats.Profile(r.Difficulty);
            int w = StageJson.WidthForMap(r.MapId), h = StageJson.HeightForMap(r.MapId);
            int time = r.TimeLimitSeconds > 0 ? r.TimeLimitSeconds : Mathf.RoundToInt(profile.TimeLimit.Median);
            float reach = BoardModel.MaxPinDistance;
            float maxLen = TensionLimits.MaxLength(TensionLimits.RestLength);

            var sb = new StringBuilder();
            sb.AppendLine("You design one level for Twisted Tangle, a rope-untangling puzzle seen from above.");
            sb.AppendLine();
            sb.AppendLine("RULES");
            sb.AppendLine($"- Board: {w} columns x {h} rows, cells x 0..{w - 1}, y 0..{h - 1}. At most one pin per cell.");
            sb.AppendLine("- A rope joins two different pins. A pin may hold several ropes (shared pin).");
            sb.AppendLine("- A rope may run through more than two pins: write it as one rope per stretch, same colorIndex, " +
                          "each stretch starting on the pin the previous one ended on.");
            sb.AppendLine("- blockedCells are closed holes: no pin starts there and no pin can be dropped there.");
            sb.AppendLine("- funnelCells hold a post: ropes wrap around it and catch on it; no pin starts or lands there.");
            sb.AppendLine("- A move drags one unlocked pin to an empty cell. Locked pins never move.");
            sb.AppendLine($"- Reach: a dragged pin stays within {reach:0.##} cells of the other pin of each of its ropes; " +
                          $"a drop that stretches a rope past {maxLen:0.##} cells is cancelled (tension).");
            sb.AppendLine("- A rope disappears once it touches no other rope for a moment AND at least one of its pins holds only that rope.");
            sb.AppendLine("- Win: every rope gone before the time limit runs out.");
            sb.AppendLine($"- Ropes are physical and slack (rest length {TensionLimits.RestLength} cells): ropes with close pins bulge sideways.");
            sb.AppendLine("- Over/under matters. A rope lying on top of another at every crossing is cleared with 1 move of the top rope " +
                          "(moving the lower rope drags the top one along). Ropes that alternate over/under are hooked: both need a move (~2 moves).");
            sb.AppendLine("- At the start every rope must touch at least one other rope. No rope may start free.");
            sb.AppendLine();
            sb.AppendLine($"TARGET ({r.Difficulty}, measured from shipped levels {profile.FromLevel}–{profile.ToLevel})");
            sb.AppendLine($"- Ropes {profile.Ropes.Ints}, pins {profile.Pins.Ints}, pin-to-pin rope length {profile.RopeLength.Cells} cells.");
            sb.AppendLine($"- Rope pairs in contact {profile.ContactPairs.Ints}, hooked pairs {profile.Hooks.Ints}, estimated moves {profile.Moves.Ints}.");
            sb.AppendLine($"- Shared pins: {Usage(profile.LevelsWithShared, profile)} (max {profile.SharedPins.Max:0} per level). " +
                          $"Locked pins: {Usage(profile.LevelsWithLocked, profile)} (max {profile.LockedPins.Max:0} per level).");
            sb.AppendLine($"- Time limit: {time} s (shipped range {profile.TimeLimit.Ints} s).");
            sb.AppendLine($"- colorIndex 0..{Mathf.Max(0, r.PaletteCount - 1)}, a different color per rope (stretches of one rope share it).");
            sb.AppendLine();
            sb.AppendLine("OUTPUT: only one JSON object, no code fences, no commentary:");
            sb.AppendLine($"{{\"difficulty\":\"{r.Difficulty}\",\"timeLimitSeconds\":{time},\"mapId\":{r.MapId},");
            sb.AppendLine(" \"pins\":[{\"id\":0,\"x\":0,\"y\":3,\"locked\":false}],");
            sb.AppendLine(" \"ropes\":[{\"id\":0,\"pinA\":0,\"pinB\":1,\"colorIndex\":0,\"layer\":0,\"bend\":0.0}],");
            sb.AppendLine(" \"crossings\":[{\"a\":0,\"b\":1,\"onTop\":[1]}],");
            sb.AppendLine(" \"blockedCells\":[{\"x\":2,\"y\":2}],");
            sb.AppendLine(" \"funnelCells\":[{\"x\":1,\"y\":4}]}");
            sb.AppendLine("- layer: higher = on top where ropes cross (used when a pair has no crossings entry).");
            sb.AppendLine("- bend: sideways bulge of the rope middle in cells, + = left of pinA→pinB, - = right, 0 = natural slack.");
            sb.AppendLine("- crossings: per rope pair, the rope id on top at each crossing in order along rope a from its pinA. " +
                          "Two different ids = hooked (needs a bend so the ropes cross twice).");
            sb.AppendLine("- Do not write rope paths; the editor builds them from this data.");

            if (r.Examples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("EXAMPLES (shipped levels in the same format; match the style, do not copy positions):");
                foreach (var example in r.Examples) sb.AppendLine(DescribeLevel(example));
            }

            sb.AppendLine();
            sb.AppendLine("BEFORE ANSWERING: check every rope length and that every rope touches another; write the move list " +
                          "(pin, from, to) and count 2 moves per hooked pair; only answer when the count fits the target.");
            return sb.ToString();
        }

        public static bool TryParseLevelJson(string json, out LevelJson level, out string error)
        {
            level = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Nothing pasted.";
                return false;
            }

            int start = json.IndexOf('{'), end = json.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "Could not find a JSON object in the pasted text.";
                return false;
            }

            AiLevelJson ai;
            try { ai = JsonUtility.FromJson<AiLevelJson>(json.Substring(start, end - start + 1)); }
            catch (Exception e)
            {
                error = "Invalid level JSON: " + e.Message;
                return false;
            }

            if (ai?.pins == null || ai.pins.Count == 0 || ai.ropes == null || ai.ropes.Count == 0)
            {
                error = "JSON needs pins and ropes.";
                return false;
            }

            var pinIds = new HashSet<int>(ai.pins.Select(p => p.id));
            var bad = ai.ropes.FirstOrDefault(r => !pinIds.Contains(r.pinA) || !pinIds.Contains(r.pinB) || r.pinA == r.pinB);
            if (bad != null)
            {
                error = $"Rope {bad.id} must join two different existing pins.";
                return false;
            }

            level = FromAiJson(ai);
            return true;
        }

        public static LevelJson FromAiJson(AiLevelJson ai)
        {
            int mapId = Mathf.Max(1, ai.mapId);
            string difficulty = string.IsNullOrEmpty(ai.difficulty) ? "Easy" : ai.difficulty;
            var stage = new StageJson
            {
                mapId = mapId,
                gridWidth = StageJson.WidthForMap(mapId),
                gridHeight = StageJson.HeightForMap(mapId),
                pins = ai.pins,
                blockedCells = ai.blockedCells ?? new List<Vector2Int>(),
                funnelCells = ai.funnelCells ?? new List<Vector2Int>(),
                ropes = ai.ropes.Select(r => new RopeJson
                    { id = r.id, pinA = r.pinA, pinB = r.pinB, colorIndex = r.colorIndex, layer = r.layer }).ToList(),
            };

            var bends = ai.ropes.ToDictionary(r => r.id, r => r.bend);
            RopePathBuilder.BuildStage(stage, r => bends.GetValueOrDefault(r.id), (a, b, rankA, rankB) =>
            {
                var c = ai.crossings?.FirstOrDefault(x => (x.a == a.id && x.b == b.id) || (x.a == b.id && x.b == a.id));
                var tops = c?.onTop?.Where(id => id == a.id || id == b.id).ToList();
                if (tops == null || tops.Count == 0)
                    return a.layer != b.layer ? (a.layer > b.layer ? a.id : b.id) : Mathf.Max(a.id, b.id);
                int rank = c.a == a.id ? rankA : rankB;
                return tops[Mathf.Min(rank, tops.Count - 1)];
            });

            return new LevelJson
            {
                difficulty = difficulty,
                timeLimitSeconds = ai.timeLimitSeconds > 0
                    ? ai.timeLimitSeconds
                    : Mathf.RoundToInt(LevelDataStats.Profile(difficulty).TimeLimit.Median),
                stages = new List<StageJson> { stage },
                tutorial = new TutorialJson(),
            };
        }

        public static AiLevelJson ToAiJson(LevelJson level)
        {
            var stage = level.stages[0];
            var ai = new AiLevelJson
            {
                difficulty = level.difficulty,
                timeLimitSeconds = level.timeLimitSeconds,
                mapId = stage.mapId,
                pins = stage.pins,
                blockedCells = stage.blockedCells,
                funnelCells = stage.funnelCells,
                ropes = stage.ropes.Select(r => new AiRopeJson
                {
                    id = r.id, pinA = r.pinA, pinB = r.pinB, colorIndex = r.colorIndex, layer = r.layer,
                    bend = MeasureBend(stage, r),
                }).ToList(),
            };
            if (LevelValidator.Validate(level).IsValid)
                foreach (var p in LevelValidator.AnalyzePairs(stage))
                    if (p.Tops.Count > 0)
                        ai.crossings.Add(new AiCrossingJson { a = p.A.id, b = p.B.id, onTop = p.Tops });
            return ai;
        }

        private static float MeasureBend(StageJson stage, RopeJson rope)
        {
            if (rope.path == null || rope.path.Count < 3) return 0f;
            Vector2 a = stage.pins.Find(p => p.id == rope.pinA).Cell, b = stage.pins.Find(p => p.id == rope.pinB).Cell;
            var chord = b - a;
            if (chord.sqrMagnitude < 1e-6f) return 0f;
            var left = new Vector2(-chord.y, chord.x).normalized;
            float bend = 0f;
            foreach (var p in rope.path)
            {
                float side = Vector2.Dot(new Vector2(p.x, p.y) - a, left);
                if (Mathf.Abs(side) > Mathf.Abs(bend)) bend = side;
            }
            return Mathf.Abs(bend) < MinBend ? 0f : Mathf.Round(bend * 10f) / 10f;
        }

        private static string ShortFloats(string json) =>
            Regex.Replace(json, @"-?\d+\.\d{3,}", m =>
                float.Parse(m.Value, CultureInfo.InvariantCulture).ToString("0.0#", CultureInfo.InvariantCulture));

        private static string Usage(int levels, DifficultyProfile p) =>
            levels == 0 ? "never" : $"{levels} of {p.Levels.Count} levels";
    }
}
