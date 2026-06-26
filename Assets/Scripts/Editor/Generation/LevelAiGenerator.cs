using System;
using System.Collections.Generic;
using System.Text;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Editor.Generation
{
    /// <summary>What the designer wants the AI to build. Filled from the editor UI.</summary>
    public sealed class LevelGenerationRequest
    {
        public int GridWidth = 6;
        public int GridHeight = 6;
        public int TimeSeconds = 45;
        public string Difficulty = "Medium";                // Easy | Medium | Hard
        public List<string> EntityTypeIds = new();          // allowed peg types
        public List<string> NailedTypeIds = new();          // immovable pin types (the solver can't move these)
        public List<string> PaletteHex = new();             // allowed rope colors as #RRGGBB
        public string ReferenceLevelDescription = null;     // null = generate freely; non-null = style inspiration
    }

    /// <summary>
    /// Provider-agnostic, editor-time AI level generation (see Docs/level-solver-design.md §3).
    /// Build a prompt with <see cref="BuildManualPrompt"/>, paste it into ANY AI chat (Claude, Gemini,
    /// ChatGPT, ...), then paste the AI's JSON answer back and turn it into a LevelDataSO via
    /// <see cref="TryParseLevelJson"/>. The designer reviews / validates / solves / commits. Never runs in a build.
    /// </summary>
    public static class LevelAiGenerator
    {
        /// <summary>
        /// Serializes a level into a short human-readable description for use as a reference prompt section.
        /// Returns null if the level is null.
        /// </summary>
        public static string DescribeLevel(LevelDataSO level)
        {
            if (level == null) return null;
            var sb = new StringBuilder();
            sb.Append($"Grid {level.GridWidth}x{level.GridHeight}, time {level.TimeSeconds}s, {level.Ropes.Count} rope(s)\n");
            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2) continue;
                var start = rope.Path[0].PegCoord;
                var end = rope.Path[^1].PegCoord;
                string color = "#" + ColorUtility.ToHtmlStringRGB(rope.Tint);
                sb.Append($"  Rope {rope.RopeId} ({color}, layer {rope.Layer}): ({start.x},{start.y})");
                for (int i = 1; i < rope.Path.Count - 1; i++)
                    sb.Append($" → via ({rope.Path[i].PegCoord.x},{rope.Path[i].PegCoord.y})");
                sb.Append($" → ({end.x},{end.y})\n");
            }
            return sb.ToString();
        }

        /// <summary>A self-contained prompt to paste into any AI chat: the rules + the exact JSON shape to return.</summary>
        public static string BuildManualPrompt(LevelGenerationRequest r)
        {
            var sb = new StringBuilder();
            sb.Append(Rules(r));
            sb.Append("\nOutput ONLY a JSON object (no markdown code fences, no commentary) in EXACTLY this shape:\n");
            sb.Append("{\n");
            sb.Append("  \"gridWidth\": <int>, \"gridHeight\": <int>, \"timeSeconds\": <int>,\n");
            sb.Append("  \"gridEntities\": [ { \"x\": <int>, \"y\": <int>, \"typeId\": \"<one of the allowed ids>\" } ],\n");
            sb.Append("  \"ropes\": [ { \"ropeId\": <int>, \"color\": \"#RRGGBB\", \"layer\": <int>, \"path\": [ { \"x\": <int>, \"y\": <int> } ] } ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>Parses pasted level JSON (tolerates surrounding prose / ```json fences) into a LevelDataSO.</summary>
        public static bool TryParseLevelJson(string json, out LevelDataSO level, out string error)
        {
            level = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "Nothing pasted."; return false; }

            string obj = ExtractJsonObject(json);
            if (obj == null) { error = "Could not find a JSON object in the pasted text."; return false; }

            LevelDto dto;
            try { dto = JsonUtility.FromJson<LevelDto>(obj); }
            catch (Exception e) { error = "Invalid level JSON: " + e.Message; return false; }

            if (dto == null) { error = "Empty level JSON."; return false; }
            level = ToLevel(dto);
            return true;
        }

        // Takes the substring from the first '{' to the last '}' — strips code fences and stray prose.
        private static string ExtractJsonObject(string s)
        {
            int start = s.IndexOf('{');
            int end = s.LastIndexOf('}');
            return start >= 0 && end > start ? s.Substring(start, end - start + 1) : null;
        }

        private static LevelDataSO ToLevel(LevelDto dto)
        {
            var level = ScriptableObject.CreateInstance<LevelDataSO>();
            level.GridWidth = Mathf.Max(1, dto.gridWidth);
            level.GridHeight = Mathf.Max(1, dto.gridHeight);
            level.TimeSeconds = Mathf.Max(1, dto.timeSeconds);

            if (dto.gridEntities != null)
                foreach (var p in dto.gridEntities)
                    level.GridEntities.Add(new GridEntityData(new Vector2Int(p.x, p.y), p.typeId));

            if (dto.ropes != null)
                foreach (var r in dto.ropes)
                {
                    var rope = new RopeData(r.ropeId, ParseColor(r.color), r.layer);
                    if (r.path != null)
                        foreach (var pt in r.path)
                            rope.Path.Add(new RopeWaypoint(new Vector2Int(pt.x, pt.y)));
                    level.Ropes.Add(rope);
                }

            return level;
        }

        private static Color ParseColor(string hex) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        /// <summary>The puzzle rules + designer context the AI must respect.</summary>
        private static string Rules(LevelGenerationRequest r)
        {
            int maxX = Mathf.Max(0, r.GridWidth - 1);
            int maxY = Mathf.Max(0, r.GridHeight - 1);
            var sb = new StringBuilder();
            sb.Append("You design levels for a rope-untangling puzzle (Twisted Tangle style).\n\nRules:\n");
            sb.Append("- The board is a grid ").Append(r.GridWidth).Append(" wide x ").Append(r.GridHeight)
              .Append(" tall. Coordinates: x in [0,").Append(maxX).Append("], y in [0,").Append(maxY).Append("].\n");
            sb.Append("- A peg sits on a grid cell. A rope connects exactly two pegs; its 'path' lists the pegs it goes through, ");
            sb.Append("with the FIRST and LAST entries being the two endpoint pegs.\n");
            sb.Append("- Each rope is a straight segment between its two endpoint pegs; the endpoints must be at most 3 units apart (max(|dx|,|dy|) <= 3).\n");
            sb.Append("- The level MUST be solvable: by moving pegs to empty cells (keeping every rope <= 3 units) it must be possible ");
            sb.Append("to reach a state where no two ropes cross. Make it tangled (several crossings) yet solvable.\n");
            sb.Append("- 'layer' is the draw order (higher = on top); give crossing ropes distinct layers.\n");
            sb.Append("- Use gridWidth=").Append(r.GridWidth).Append(", gridHeight=").Append(r.GridHeight)
              .Append(", timeSeconds=").Append(r.TimeSeconds).Append(".\n");
            sb.Append("- Target difficulty: ").Append(r.Difficulty)
              .Append(" (Easy ~ 2-3 ropes / few crossings, Medium ~ 3-4, Hard ~ 5+ with denser crossings).\n");
            if (r.EntityTypeIds is { Count: > 0 })
                sb.Append("- Use ONLY these peg typeId values: ").Append(string.Join(", ", r.EntityTypeIds)).Append(".\n");
            if (r.NailedTypeIds is { Count: > 0 })
                sb.Append("- These pin types are NAILED (immovable — the solver can never move them): ")
                  .Append(string.Join(", ", r.NailedTypeIds))
                  .Append(". A rope may have 0, 1, or 2 nailed endpoints. Keep the level solvable: a rope with BOTH endpoints nailed can only be cleared by moving OTHER ropes out of its way, so don't trap such ropes in unavoidable crossings.\n");
            if (r.PaletteHex is { Count: > 0 })
                sb.Append("- Use rope colors from this palette (hex): ").Append(string.Join(", ", r.PaletteHex))
                  .Append(". Prefer a distinct color per rope.\n");
            sb.Append("- Every rope endpoint and waypoint must land on an entity you also list in 'gridEntities'. No two entities share a cell.\n");
            if (!string.IsNullOrEmpty(r.ReferenceLevelDescription))
            {
                sb.Append("\nReference level (use as inspiration for rope count, crossing density, and layout style — do NOT copy pin positions exactly):\n");
                sb.Append(r.ReferenceLevelDescription);
            }
            return sb.ToString();
        }

        // --- DTOs (JsonUtility-friendly: public fields, [Serializable]) ---
        [Serializable] private class LevelDto { public int gridWidth; public int gridHeight; public int timeSeconds; public PegDto[] gridEntities; public RopeDto[] ropes; }
        [Serializable] private class PegDto { public int x; public int y; public string typeId; }
        [Serializable] private class RopeDto { public int ropeId; public string color; public int layer; public PointDto[] path; }
        [Serializable] private class PointDto { public int x; public int y; }
    }
}
