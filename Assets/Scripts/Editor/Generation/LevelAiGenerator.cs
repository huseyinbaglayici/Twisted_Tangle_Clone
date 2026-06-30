using System;
using System.Collections.Generic;
using System.Text;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Editor.Generation
{
    public sealed class LevelGenerationRequest
    {
        public int GridWidth = 6;
        public int GridHeight = 6;
        public int TimeSeconds = 45;
        public string Difficulty = "Medium";
        public List<string> EntityTypeIds = new();
        public List<string> NailedTypeIds = new();
        public List<string> PaletteHex = new();
        public string ReferenceLevelDescription = null;
    }

    // Concrete per-difficulty targets scaled to grid size.
    // All numbers are guidelines the AI must respect; the validator will catch violations.
    internal readonly struct DifficultyProfile
    {
        public readonly int RopeMin, RopeMax;
        public readonly int CrossingMin, CrossingMax;
        public readonly int PegMin, PegMax;
        public readonly int FreeCellPercent;    // minimum % of grid cells that must stay empty
        public readonly int MaxNailedEndpoints; // nailed pins allowed across all ropes
        public readonly int MaxSolveMoves;      // approx. upper bound on moves to untangle
        public readonly bool AllowSharedPegs;   // a peg serving as endpoint for 2+ ropes
        public readonly string TopologyHint;    // crossing arrangement guidance
        public readonly string SolveHint;       // solution shape guidance

        public DifficultyProfile(
            int ropeMin, int ropeMax,
            int crossingMin, int crossingMax,
            int pegMin, int pegMax,
            int freeCellPercent,
            int maxNailedEndpoints,
            int maxSolveMoves,
            bool allowSharedPegs,
            string topologyHint,
            string solveHint)
        {
            RopeMin = ropeMin; RopeMax = ropeMax;
            CrossingMin = crossingMin; CrossingMax = crossingMax;
            PegMin = pegMin; PegMax = pegMax;
            FreeCellPercent = freeCellPercent;
            MaxNailedEndpoints = maxNailedEndpoints;
            MaxSolveMoves = maxSolveMoves;
            AllowSharedPegs = allowSharedPegs;
            TopologyHint = topologyHint;
            SolveHint = solveHint;
        }
    }

    public static class LevelAiGenerator
    {
        // Returns a profile appropriate for the difficulty label and grid area.
        private static DifficultyProfile GetProfile(string difficulty, int gridCells)
        {
            // Scale rope/peg counts slightly for larger grids so the level fills the space.
            int bonus = gridCells >= 49 ? 1 : 0; // extra rope on 7x7+

            return difficulty switch
            {
                "Easy" => new DifficultyProfile(
                    ropeMin: 2,         ropeMax: 3 + bonus,
                    crossingMin: 1,     crossingMax: 3,
                    pegMin: 4,          pegMax: 6 + bonus,
                    freeCellPercent: 45,
                    maxNailedEndpoints: 0,
                    maxSolveMoves: 2,
                    allowSharedPegs: false,
                    topologyHint: "LINEAR — each rope crosses at most one other rope. Example: A×B and B×C is fine; A×B, A×C and B×C (star/triangle) is NOT.",
                    solveHint: "A single peg move should resolve every crossing. If not, two sequential moves must do it."
                ),
                "Hard" => new DifficultyProfile(
                    ropeMin: 4 + bonus, ropeMax: 6 + bonus,
                    crossingMin: 5,     crossingMax: 9,
                    pegMin: 7 + bonus,  pegMax: 11 + bonus,
                    freeCellPercent: 25,
                    maxNailedEndpoints: 3,
                    maxSolveMoves: 6,
                    allowSharedPegs: true,
                    topologyHint: "NESTED — several ropes share pegs and cross each other in a 'star' or 'web' pattern. Moving one peg affects multiple ropes, so the order of moves matters.",
                    solveHint: "The solution requires 4–6 ordered moves. Some moves free space for later moves. There is no shortcut — every move is necessary."
                ),
                _ => new DifficultyProfile( // Medium
                    ropeMin: 3,         ropeMax: 4 + bonus,
                    crossingMin: 3,     crossingMax: 6,
                    pegMin: 5,          pegMax: 8 + bonus,
                    freeCellPercent: 35,
                    maxNailedEndpoints: 1,
                    maxSolveMoves: 4,
                    allowSharedPegs: true,
                    topologyHint: "MIXED — one or two 'hub' pegs serve two ropes each, creating a moderate dependency. Some crossings are nested, some linear.",
                    solveHint: "2–4 moves resolve all crossings. At least one move must be made in the right order before another becomes possible."
                ),
            };
        }

        public static string DescribeLevel(LevelDataSO level)
        {
            if (level == null) return null;
            var sb = new StringBuilder();
            sb.Append($"Grid {level.GridWidth}x{level.GridHeight}, time {level.TimeSeconds}s, {level.Ropes.Count} rope(s)\n");
            foreach (var rope in level.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2) continue;
                var start = rope.Path[0].PegCoord;
                var end   = rope.Path[^1].PegCoord;
                string color = "#" + ColorUtility.ToHtmlStringRGB(rope.Tint);
                sb.Append($"  Rope {rope.RopeId} ({color}, layer {rope.Layer}): ({start.x},{start.y})");
                for (int i = 1; i < rope.Path.Count - 1; i++)
                    sb.Append($" → via ({rope.Path[i].PegCoord.x},{rope.Path[i].PegCoord.y})");
                sb.Append($" → ({end.x},{end.y})\n");
            }
            return sb.ToString();
        }

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

        private static string ExtractJsonObject(string s)
        {
            int start = s.IndexOf('{');
            int end   = s.LastIndexOf('}');
            return start >= 0 && end > start ? s.Substring(start, end - start + 1) : null;
        }

        private static LevelDataSO ToLevel(LevelDto dto)
        {
            var level = ScriptableObject.CreateInstance<LevelDataSO>();
            level.GridWidth   = Mathf.Max(1, dto.gridWidth);
            level.GridHeight  = Mathf.Max(1, dto.gridHeight);
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

        private static string Rules(LevelGenerationRequest r)
        {
            int maxX = Mathf.Max(0, r.GridWidth  - 1);
            int maxY = Mathf.Max(0, r.GridHeight - 1);
            int cells = r.GridWidth * r.GridHeight;
            int minFree = Mathf.CeilToInt(cells * 0.01f); // computed below from profile
            var p = GetProfile(r.Difficulty, cells);

            var sb = new StringBuilder();

            // ── Game mechanics ───────────────────────────────────────────────
            sb.AppendLine("You design levels for a physics-based rope-untangling puzzle called Twisted Tangle.");
            sb.AppendLine();
            sb.AppendLine("GAME MECHANICS:");
            sb.Append("- Grid is ").Append(r.GridWidth).Append(" wide × ").Append(r.GridHeight)
              .Append(" tall. Coordinates: x∈[0,").Append(maxX).Append("], y∈[0,").Append(maxY).Append("].\n");
            sb.AppendLine("- A PEG sits on a grid cell. A ROPE is a straight segment connecting exactly two peg endpoints.");
            sb.AppendLine("- The player drags pegs to empty cells to untangle the ropes. Ropes stretch/compress but are always straight between their two endpoints.");
            sb.AppendLine("- A move = drag one peg from its cell to a different empty cell.");
            sb.AppendLine("- REACH LIMIT: both endpoints of every rope must always satisfy max(|dx|,|dy|) ≤ 3 (Chebyshev distance). A move that would violate this for any rope attached to the dragged peg is illegal.");
            sb.AppendLine("- WIN CONDITION: reach a state where no two rope segments intersect.");
            sb.AppendLine("- 'layer' controls draw order (higher = drawn on top). Crossing ropes must have distinct layers.");
            sb.AppendLine("- Every peg that appears as a rope endpoint must also appear in gridEntities. No two pegs share a cell.");
            sb.AppendLine();

            // ── Difficulty targets ───────────────────────────────────────────
            int freeCellMin = Mathf.CeilToInt(cells * p.FreeCellPercent / 100f);
            sb.Append("DIFFICULTY: ").AppendLine(r.Difficulty.ToUpperInvariant());
            sb.AppendLine("You MUST hit ALL of these targets:");
            sb.Append("  • Ropes: ").Append(p.RopeMin).Append("–").AppendLine(p.RopeMax.ToString());
            sb.Append("  • Crossings in the INITIAL layout: ").Append(p.CrossingMin).Append("–").AppendLine(p.CrossingMax.ToString());
            sb.Append("  • Total pegs (gridEntities): ").Append(p.PegMin).Append("–").AppendLine(p.PegMax.ToString());
            sb.Append("  • Empty cells (grid cells with no peg): at least ").Append(freeCellMin)
              .Append(" (≥").Append(p.FreeCellPercent).AppendLine("% of grid). Players need room to drag pegs.");
            sb.Append("  • Max nailed-pin endpoints across all ropes: ").AppendLine(p.MaxNailedEndpoints.ToString());
            sb.Append("  • Approximate moves to solve: ≤").AppendLine(p.MaxSolveMoves.ToString());
            sb.Append("  • Shared pegs (one peg as endpoint for 2+ ropes): ")
              .AppendLine(p.AllowSharedPegs ? "ALLOWED — use 1–2 hub pegs to increase interdependency." : "NOT allowed — keep ropes independent.");
            sb.AppendLine();

            // ── Topology guidance ────────────────────────────────────────────
            sb.AppendLine("CROSSING TOPOLOGY:");
            sb.AppendLine(p.TopologyHint);
            sb.AppendLine();

            // ── Solvability guidance ─────────────────────────────────────────
            sb.AppendLine("SOLVABILITY:");
            sb.AppendLine("- The initial layout must have crossings (tangled). The SOLVED layout must have zero crossings.");
            sb.AppendLine("- Every legal move must keep all ropes within reach (max(|dx|,|dy|) ≤ 3). Plan around this constraint.");
            sb.AppendLine("- Nailed pegs CANNOT be moved. A rope with BOTH endpoints nailed can only be cleared by moving other ropes away — do not trap such ropes in unavoidable crossings.");
            sb.AppendLine(p.SolveHint);
            sb.AppendLine();

            // ── Fixed values ─────────────────────────────────────────────────
            sb.Append("USE: gridWidth=").Append(r.GridWidth)
              .Append(", gridHeight=").Append(r.GridHeight)
              .Append(", timeSeconds=").Append(r.TimeSeconds).AppendLine(".");

            if (r.EntityTypeIds is { Count: > 0 })
                sb.Append("Allowed peg typeId values: ").AppendLine(string.Join(", ", r.EntityTypeIds));

            if (r.NailedTypeIds is { Count: > 0 })
                sb.Append("NAILED (immovable) types: ").AppendLine(string.Join(", ", r.NailedTypeIds));

            if (r.PaletteHex is { Count: > 0 })
                sb.Append("Rope colors (use distinct color per rope): ").AppendLine(string.Join(", ", r.PaletteHex));

            if (!string.IsNullOrEmpty(r.ReferenceLevelDescription))
            {
                sb.AppendLine();
                sb.AppendLine("REFERENCE LEVEL (use as style inspiration — do NOT copy positions exactly):");
                sb.AppendLine(r.ReferenceLevelDescription);
            }

            // ── Chain-of-thought instruction ─────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("THINK STEP BY STEP before producing JSON:");
            sb.AppendLine("  Step 1 — Place pegs: Choose positions on the grid. Verify the empty-cell count.");
            sb.AppendLine("  Step 2 — Draw ropes: Connect peg pairs. Check reach (max(|dx|,|dy|) ≤ 3) for every rope.");
            sb.AppendLine("  Step 3 — Count crossings: Verify the crossing count is in the target range.");
            sb.AppendLine("  Step 4 — Simulate solution: Write out the move sequence (which peg, to where) that untangles all ropes.");
            sb.AppendLine("           Confirm every move is legal (target cell is empty, all ropes stay within reach after the move).");
            sb.AppendLine("  Step 5 — Verify empty cells after each move: a peg vacates its old cell (becomes empty) and occupies the new cell.");
            sb.AppendLine("  Step 6 — Only if every step checks out, emit the JSON.");
            sb.AppendLine("If you cannot find a valid solution in Step 4, redesign the layout.");

            return sb.ToString();
        }

        [Serializable] private class LevelDto  { public int gridWidth; public int gridHeight; public int timeSeconds; public PegDto[] gridEntities; public RopeDto[] ropes; }
        [Serializable] private class PegDto    { public int x; public int y; public string typeId; }
        [Serializable] private class RopeDto   { public int ropeId; public string color; public int layer; public PointDto[] path; }
        [Serializable] private class PointDto  { public int x; public int y; }
    }
}
