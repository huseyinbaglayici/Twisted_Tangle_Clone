using System;
using System.Collections.Generic;
using System.Text;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;
using UnityEngine.Networking;

namespace TwistedTangle.Editor.Generation
{
    /// <summary>What the designer wants the AI to build. Filled from the editor UI.</summary>
    public sealed class LevelGenerationRequest
    {
        public int GridWidth = 6;
        public int GridHeight = 6;
        public int TimeSeconds = 45;
        public string Difficulty = "Medium";        // Easy | Medium | Hard
        public List<string> EntityTypeIds = new();   // allowed peg types
        public List<string> PaletteHex = new();       // allowed rope colors as #RRGGBB
        public string Model = "claude-sonnet-4-6";
        public int MaxTokens = 8000;
    }

    /// <summary>
    /// Editor-time AI level generation (see Docs/level-solver-design.md §3). Two free-or-cheap paths,
    /// both producing a LevelDataSO the designer reviews/validates/solves/commits — never runs in a build:
    ///   • Manual (free, uses Claude Pro): <see cref="BuildManualPrompt"/> → paste into claude.ai → paste
    ///     the JSON answer back → <see cref="TryParseLevelJson"/>.
    ///   • Live API (needs ANTHROPIC_API_KEY + credit): <see cref="Generate"/> calls the Messages API with
    ///     structured outputs.
    /// </summary>
    public static class LevelAiGenerator
    {
        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        // json_schema for the live-API structured output — guarantees the response shape. Mirrors LevelDto.
        private const string LevelSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
            "\"gridWidth\":{\"type\":\"integer\"}," +
            "\"gridHeight\":{\"type\":\"integer\"}," +
            "\"timeSeconds\":{\"type\":\"integer\"}," +
            "\"pegs\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"},\"typeId\":{\"type\":\"string\"}}," +
            "\"required\":[\"x\",\"y\",\"typeId\"]}}," +
            "\"ropes\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"properties\":{\"ropeId\":{\"type\":\"integer\"},\"color\":{\"type\":\"string\"},\"layer\":{\"type\":\"integer\"}," +
            "\"path\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}},\"required\":[\"x\",\"y\"]}}}," +
            "\"required\":[\"ropeId\",\"color\",\"layer\",\"path\"]}}" +
            "},\"required\":[\"gridWidth\",\"gridHeight\",\"timeSeconds\",\"pegs\",\"ropes\"]}";

        // ---------------------------------------------------------------- manual (free) path

        /// <summary>A self-contained prompt to paste into claude.ai: the rules + the exact JSON shape to return.</summary>
        public static string BuildManualPrompt(LevelGenerationRequest r)
        {
            var sb = new StringBuilder();
            sb.Append(Rules(r));
            sb.Append("\nOutput ONLY a JSON object (no markdown code fences, no commentary) in EXACTLY this shape:\n");
            sb.Append("{\n");
            sb.Append("  \"gridWidth\": <int>, \"gridHeight\": <int>, \"timeSeconds\": <int>,\n");
            sb.Append("  \"pegs\": [ { \"x\": <int>, \"y\": <int>, \"typeId\": \"<one of the allowed ids>\" } ],\n");
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

        // ---------------------------------------------------------------- live API path

        /// <summary>Fires <paramref name="onSuccess"/> (or <paramref name="onError"/>) on the main thread when done.</summary>
        public static void Generate(LevelGenerationRequest request,
            Action<LevelDataSO> onSuccess, Action<string> onError)
        {
            string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                onError?.Invoke("ANTHROPIC_API_KEY environment variable is not set.");
                return;
            }

            string prompt = Rules(request) + "\nOutput ONLY a level object matching the provided JSON schema.";
            string body =
                "{\"model\":\"" + request.Model + "\"," +
                "\"max_tokens\":" + request.MaxTokens + "," +
                "\"messages\":[{\"role\":\"user\",\"content\":\"" + JsonEscape(prompt) + "\"}]," +
                "\"output_config\":{\"format\":{\"type\":\"json_schema\",\"schema\":" + LevelSchema + "}}}";

            var req = new UnityWebRequest(Endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("content-type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", AnthropicVersion);

            req.SendWebRequest().completed += _ =>
            {
                try { HandleResponse(req, onSuccess, onError); }
                finally { req.Dispose(); }
            };
        }

        private static void HandleResponse(UnityWebRequest req,
            Action<LevelDataSO> onSuccess, Action<string> onError)
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"HTTP {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
                return;
            }

            AnthropicResponse resp;
            try { resp = JsonUtility.FromJson<AnthropicResponse>(req.downloadHandler.text); }
            catch (Exception e) { onError?.Invoke("Could not parse API response: " + e.Message); return; }

            if (resp?.content == null || resp.content.Length == 0)
            {
                onError?.Invoke($"Empty response (stop_reason: {resp?.stop_reason}).");
                return;
            }
            if (resp.stop_reason == "refusal")
            {
                onError?.Invoke("The model declined the request (refusal).");
                return;
            }

            if (TryParseLevelJson(resp.content[0].text, out var level, out var error)) onSuccess?.Invoke(level);
            else onError?.Invoke(error + "\n" + resp.content[0].text);
        }

        // ---------------------------------------------------------------- shared

        private static LevelDataSO ToLevel(LevelDto dto)
        {
            var level = ScriptableObject.CreateInstance<LevelDataSO>();
            level.GridWidth = Mathf.Max(1, dto.gridWidth);
            level.GridHeight = Mathf.Max(1, dto.gridHeight);
            level.TimeSeconds = Mathf.Max(1, dto.timeSeconds);

            if (dto.pegs != null)
                foreach (var p in dto.pegs)
                    level.Pegs.Add(new PegData(new Vector2Int(p.x, p.y), p.typeId));

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

        /// <summary>The puzzle rules + designer context, shared by both the manual and API prompts.</summary>
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
            if (r.PaletteHex is { Count: > 0 })
                sb.Append("- Use rope colors from this palette (hex): ").Append(string.Join(", ", r.PaletteHex))
                  .Append(". Prefer a distinct color per rope.\n");
            sb.Append("- Every rope endpoint and waypoint must land on a peg you also list in 'pegs'. No two pegs share a cell.\n");
            return sb.ToString();
        }

        // Minimal JSON string escaper for the API prompt content.
        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // --- DTOs (JsonUtility-friendly: public fields, [Serializable]) ---
        [Serializable] private class AnthropicResponse { public ContentBlock[] content; public string stop_reason; }
        [Serializable] private class ContentBlock { public string type; public string text; }
        [Serializable] private class LevelDto { public int gridWidth; public int gridHeight; public int timeSeconds; public PegDto[] pegs; public RopeDto[] ropes; }
        [Serializable] private class PegDto { public int x; public int y; public string typeId; }
        [Serializable] private class RopeDto { public int ropeId; public string color; public int layer; public PointDto[] path; }
        [Serializable] private class PointDto { public int x; public int y; }
    }
}
