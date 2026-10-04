using System;
using System.Text;
using Runtime.Level;
using UnityEngine;

namespace TwistedTangle.Editor.Generation
{
    public sealed class LevelGenerationRequest
    {
        public int MapId = 1;
        public int MoveLimit;
        public string Difficulty = "Easy";
        public int PaletteCount = 8;
        public string ReferenceLevelDescription;
    }

    // Per-difficulty targets, following the level curve (levels 1-4: 2 ropes, 5-10: 3, 11-30: 4-5).
    internal readonly struct DifficultyProfile
    {
        public readonly int RopeMin, RopeMax;
        public readonly int MaxSolveMoves;
        public readonly bool SharedPins;
        public readonly string Hint;

        public DifficultyProfile(int ropeMin, int ropeMax, int maxSolveMoves, bool sharedPins, string hint)
        {
            RopeMin = ropeMin;
            RopeMax = ropeMax;
            MaxSolveMoves = maxSolveMoves;
            SharedPins = sharedPins;
            Hint = hint;
        }
    }

    /// <summary>
    /// Provider-agnostic AI level generation: builds a prompt for any AI chat and parses the JSON answer
    /// straight into the runtime level format (LevelJson), so an imported level is exactly what the game loads.
    /// </summary>
    public static class LevelAiGenerator
    {
        private static DifficultyProfile GetProfile(string difficulty) => difficulty switch
        {
            "Hard" => new DifficultyProfile(5, 6, 8, true,
                "Several ropes cross each other in a web; at least one pin holds two ropes, so the order of moves matters."),
            "Medium" => new DifficultyProfile(4, 5, 5, false,
                "Ropes cross in a chain: freeing one rope makes room to free the next."),
            _ => new DifficultyProfile(2, 3, 3, false,
                "A simple tangle: one or two well-chosen moves free the ropes."),
        };

        public static string DescribeLevel(LevelJson level)
        {
            if (level?.stages == null || level.stages.Count == 0) return null;
            var stage = level.stages[0];
            var sb = new StringBuilder();
            sb.Append($"Map {stage.mapId} ({stage.gridWidth}x{stage.gridHeight}), {stage.pins.Count} pins, {stage.ropes.Count} ropes\n");
            foreach (var pin in stage.pins)
                sb.Append($"  Pin {pin.id}: ({pin.x},{pin.y}){(pin.locked ? " locked" : "")}\n");
            foreach (var rope in stage.ropes)
                sb.Append($"  Rope {rope.id}: pin {rope.pinA} → pin {rope.pinB} (color {rope.colorIndex}, layer {rope.layer})\n");
            return sb.ToString();
        }

        public static string BuildManualPrompt(LevelGenerationRequest r)
        {
            int w = StageJson.WidthForMap(r.MapId), h = StageJson.HeightForMap(r.MapId);
            var sb = new StringBuilder();
            sb.Append(Rules(r, w, h));
            sb.Append("\nOutput ONLY a JSON object (no markdown code fences, no commentary) in EXACTLY this shape:\n");
            sb.Append("{\n");
            sb.Append($"  \"difficulty\": \"{r.Difficulty}\", \"moveLimit\": {r.MoveLimit}, \"timeLimitSeconds\": 0,\n");
            sb.Append($"  \"stages\": [ {{ \"mapId\": {r.MapId}, \"gridWidth\": {w}, \"gridHeight\": {h},\n");
            sb.Append("    \"pins\":  [ { \"id\": <int>, \"x\": <int>, \"y\": <int>, \"locked\": <bool> } ],\n");
            sb.Append("    \"ropes\": [ { \"id\": <int>, \"pinA\": <pin id>, \"pinB\": <pin id>, \"colorIndex\": <int>, \"layer\": <int> } ]\n");
            sb.Append("  } ]\n");
            sb.Append("}\n");
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

            string obj = ExtractJsonObject(json);
            if (obj == null)
            {
                error = "Could not find a JSON object in the pasted text.";
                return false;
            }

            try { level = LevelJson.FromJson(obj); }
            catch (Exception e)
            {
                error = "Invalid level JSON: " + e.Message;
                return false;
            }

            if (level?.stages == null || level.stages.Count == 0)
            {
                error = "JSON has no stages.";
                return false;
            }

            foreach (var stage in level.stages)
            {
                if (stage.mapId < 1) stage.mapId = 1;
                if (stage.gridWidth <= 0) stage.gridWidth = StageJson.WidthForMap(stage.mapId);
                if (stage.gridHeight <= 0) stage.gridHeight = StageJson.HeightForMap(stage.mapId);
            }
            level.tutorial ??= new TutorialJson();
            if (string.IsNullOrEmpty(level.difficulty)) level.difficulty = "Easy";
            return true;
        }

        private static string ExtractJsonObject(string s)
        {
            int start = s.IndexOf('{');
            int end = s.LastIndexOf('}');
            return start >= 0 && end > start ? s.Substring(start, end - start + 1) : null;
        }

        private static string Rules(LevelGenerationRequest r, int w, int h)
        {
            var p = GetProfile(r.Difficulty);
            float maxLen = RopeTensionRule.MaxLength(RopeTensionRule.RestLength);
            float warnLen = RopeTensionRule.RestLength * (1f + RopeTensionRule.WarnPercentage * RopeTensionRule.MaxTension);

            var sb = new StringBuilder();
            sb.AppendLine("You design levels for the rope-untangling puzzle Twisted Tangle.");
            sb.AppendLine();
            sb.AppendLine("GAME RULES:");
            sb.AppendLine($"- The board is {w} columns × {h} rows. Cells: x∈[0,{w - 1}], y∈[0,{h - 1}]. Every cell is a hole.");
            sb.AppendLine("- A PIN sits in a hole; no two pins share a cell. 'locked' pins can never be moved.");
            sb.AppendLine("- A ROPE connects two different pins (pinA, pinB). A pin may hold more than one rope.");
            sb.AppendLine($"- ROPE LENGTH: the straight distance between a rope's two pins must stay ≤ {maxLen:0.00} cells " +
                          $"(beyond {warnLen:0.00} the rope is tense). Keep initial ropes ≤ {warnLen:0.0}.");
            sb.AppendLine("- MOVE = drag one unlocked pin to an empty hole, keeping all its ropes within the length limit.");
            sb.AppendLine("- A rope that touches NO other rope is free and disappears, if at least one of its pins holds only that rope.");
            sb.AppendLine("- WIN = every rope has disappeared. Treat ropes as straight segments between their pins.");
            sb.AppendLine("- 'layer' is draw order only (higher = drawn on top); it does not change the rules.");
            sb.AppendLine();
            sb.Append("DIFFICULTY: ").AppendLine(r.Difficulty.ToUpperInvariant());
            sb.AppendLine($"  • Ropes: {p.RopeMin}–{p.RopeMax}; pins: 2 per rope{(p.SharedPins ? " (1–2 pins may be shared by two ropes)" : ", no shared pins")}.");
            sb.AppendLine($"  • Initially EVERY rope must touch at least one other rope (nothing is free at the start).");
            sb.AppendLine($"  • Solvable in ≤ {p.MaxSolveMoves} moves.");
            sb.AppendLine($"  • {p.Hint}");
            if (r.MoveLimit > 0) sb.AppendLine($"  • Move limit is {r.MoveLimit}: the solution must fit in it.");
            sb.AppendLine($"  • colorIndex: 0..{Mathf.Max(0, r.PaletteCount - 1)}, a different color per rope.");
            sb.AppendLine("  • Use 0–1 locked pins.");

            if (!string.IsNullOrEmpty(r.ReferenceLevelDescription))
            {
                sb.AppendLine();
                sb.AppendLine("REFERENCE LEVEL (style inspiration — do NOT copy positions exactly):");
                sb.AppendLine(r.ReferenceLevelDescription);
            }

            sb.AppendLine();
            sb.AppendLine("THINK STEP BY STEP before producing JSON:");
            sb.AppendLine("  1 — Place pins and connect ropes; check every rope length.");
            sb.AppendLine("  2 — Check that every rope touches another rope at the start.");
            sb.AppendLine("  3 — Write the solving move sequence (pin, from, to); each target hole empty, lengths legal.");
            sb.AppendLine("  4 — Only if every step checks out, emit the JSON. Otherwise redesign.");
            return sb.ToString();
        }
    }
}
