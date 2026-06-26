using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Editor.Generation;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Dedicated window for AI-assisted level generation.
    /// Build a prompt → paste into any AI chat → paste JSON back → import into the Level Creator.
    /// State persists across Unity sessions via EditorPrefs; Reset button restores defaults.
    /// </summary>
    public class AiLevelGeneratorWindow : EditorWindow
    {
        // EditorPrefs keys — scoped per project so two projects don't share state
        private static string P(string key) => $"TwistedTangle.AiGen.{PlayerSettings.productGUID}.{key}";

        // --- data-driven content ---
        private readonly List<EntityBaseTypeSO>       _baseTypes  = new();
        private readonly List<EntityDefinitionSO>     _entityDefs = new();
        private readonly List<(string name, Color color)> _swatches = new();
        private readonly HashSet<string> _excludedTypeIds = new();

        // --- ui refs ---
        private IntegerField  _gridWidth, _gridHeight, _timeSeconds, _refLevelId;
        private DropdownField _difficulty;
        private Label         _refLabel, _statusLabel;
        private VisualElement _entitiesContainer;
        private TextField     _jsonField;
        private LevelDataSO   _refLevel;

        [MenuItem("TwistedTangle/AI Level Generator")]
        public static void ShowWindow()
        {
            var w = GetWindow<AiLevelGeneratorWindow>();
            w.titleContent = new GUIContent("AI Level Generator");
            w.minSize = new Vector2(540, 620);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("tt-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = new Color(0.102f, 0.102f, 0.102f);

            Refresh();
            LoadPrefs(); // restore field values + excluded set before building UI

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("tt-right-scroll");
            scroll.style.flexGrow      = 1;
            scroll.style.paddingTop    = 8;
            scroll.style.paddingBottom = 8;
            scroll.style.paddingLeft   = 10;
            scroll.style.paddingRight  = 10;

            scroll.Add(BuildSettingsSection());
            scroll.Add(BuildPromptSection());
            scroll.Add(BuildImportSection());

            root.Add(scroll);
            root.Add(BuildResetFooter());
        }

        private void OnDisable() => SavePrefs();

        // ── Persistence ───────────────────────────────────────────────────────

        private void LoadPrefs()
        {
            // Excluded type ids come from prefs; RebuildEntities will read _excludedTypeIds later
            var raw = EditorPrefs.GetString(P("Excluded"), string.Empty);
            _excludedTypeIds.Clear();
            foreach (var id in raw.Split(','))
                if (!string.IsNullOrEmpty(id.Trim())) _excludedTypeIds.Add(id.Trim());
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(P("W"),      _gridWidth?.value    ?? 6);
            EditorPrefs.SetInt(P("H"),      _gridHeight?.value   ?? 6);
            EditorPrefs.SetInt(P("Time"),   _timeSeconds?.value  ?? 45);
            EditorPrefs.SetString(P("Diff"), _difficulty?.value  ?? "Medium");
            EditorPrefs.SetInt(P("RefId"),  _refLevelId?.value   ?? 0);
            EditorPrefs.SetString(P("Json"), _jsonField?.value   ?? string.Empty);
            EditorPrefs.SetString(P("Excluded"), string.Join(",", _excludedTypeIds));
        }

        private void ResetPrefs()
        {
            EditorPrefs.DeleteKey(P("W"));
            EditorPrefs.DeleteKey(P("H"));
            EditorPrefs.DeleteKey(P("Time"));
            EditorPrefs.DeleteKey(P("Diff"));
            EditorPrefs.DeleteKey(P("RefId"));
            EditorPrefs.DeleteKey(P("Json"));
            EditorPrefs.DeleteKey(P("Excluded"));

            // Apply defaults immediately to live fields
            if (_gridWidth   != null) _gridWidth.value   = 6;
            if (_gridHeight  != null) _gridHeight.value  = 6;
            if (_timeSeconds != null) _timeSeconds.value = 45;
            if (_difficulty  != null) _difficulty.value  = "Medium";
            if (_refLevelId  != null) _refLevelId.value  = 0;
            if (_jsonField   != null) _jsonField.value   = string.Empty;
            _refLevel = null;
            if (_refLabel   != null) _refLabel.text = "No reference — AI generates freely.";
            if (_statusLabel != null) _statusLabel.text = " ";
            _excludedTypeIds.Clear();
            RebuildEntities();
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private VisualElement BuildSettingsSection()
        {
            var s = MakeSection("Generation settings");

            var gridRow = MakeRow();
            gridRow.AddToClassList("tt-row--wrap");
            _gridWidth   = CompactInt("Width",    EditorPrefs.GetInt(P("W"),    6));
            _gridHeight  = CompactInt("Height",   EditorPrefs.GetInt(P("H"),    6));
            _timeSeconds = CompactInt("Time(s)",  EditorPrefs.GetInt(P("Time"), 45));
            gridRow.Add(_gridWidth);
            gridRow.Add(_gridHeight);
            gridRow.Add(_timeSeconds);
            s.Add(gridRow);

            var diffRow = MakeRow();
            string savedDiff = EditorPrefs.GetString(P("Diff"), "Medium");
            var choices = new List<string> { "Easy", "Medium", "Hard" };
            int diffIdx = Mathf.Max(0, choices.IndexOf(savedDiff));
            _difficulty = new DropdownField("Difficulty", choices, diffIdx);
            _difficulty.labelElement.style.minWidth = 0;
            _difficulty.labelElement.style.width    = StyleKeyword.Auto;
            _difficulty.style.flexShrink = 0;
            diffRow.Add(_difficulty);
            s.Add(diffRow);

            var refRow = MakeRow();
            refRow.AddToClassList("tt-row--wrap");
            _refLevelId = CompactInt("Ref level", EditorPrefs.GetInt(P("RefId"), 0));
            _refLevelId.Q<Label>().style.minWidth = 0;
            _refLevelId.Q<Label>().style.width    = StyleKeyword.Auto;
            _refLevelId.tooltip = "Optional: enter a level ID and click Load. The AI will mimic its style.";
            refRow.Add(_refLevelId);
            refRow.Add(Btn("Load", LoadReference, null));
            s.Add(refRow);

            _refLabel = new Label("No reference — AI generates freely.");
            _refLabel.AddToClassList("tt-hint");
            s.Add(_refLabel);

            // ── Entity types sub-foldout (collapsed by default) ───────────────
            var sub = new Foldout { text = "Entity types", value = false };
            sub.AddToClassList("tt-subgroup");
            sub.style.marginTop = 6;

            var refreshBtn = Btn("↻ Refresh / Fetch", RefreshAndRebuild, "tt-tool");
            refreshBtn.style.alignSelf = Align.FlexStart;
            refreshBtn.style.marginBottom = 4;
            sub.Add(refreshBtn);

            _entitiesContainer = new VisualElement();
            sub.Add(_entitiesContainer);
            RebuildEntities();

            s.Add(sub);
            return s;
        }

        private void RebuildEntities()
        {
            if (_entitiesContainer == null) return;
            _entitiesContainer.Clear();

            if (_entityDefs.Count == 0)
            {
                _entitiesContainer.Add(new Label("No entity types found. Click ↻ Refresh."));
                return;
            }

            var groups = new Dictionary<string, (Color accent, List<EntityDefinitionSO> defs)>();
            var order  = new List<string>();
            foreach (var def in _entityDefs)
            {
                string g = def.BaseType != null ? def.BaseType.DisplayName : "Ungrouped";
                Color  a = def.BaseType != null ? def.BaseType.EditorColor  : new Color(0.5f, 0.5f, 0.5f);
                if (!groups.ContainsKey(g)) { groups[g] = (a, new List<EntityDefinitionSO>()); order.Add(g); }
                groups[g].defs.Add(def);
            }

            foreach (string g in order)
            {
                var (accent, defs) = groups[g];
                var header = new Label(g);
                header.AddToClassList("tt-ai-group-header");
                header.style.borderLeftColor = accent;
                _entitiesContainer.Add(header);

                foreach (var def in defs.OrderBy(d => LevelCreator.IsNailed(d) ? 1 : 0).ThenBy(d => d.DisplayName))
                {
                    string id        = def.TypeId;
                    bool   mandatory = !LevelCreator.IsNailed(def);
                    var    item      = MakeRow();
                    item.AddToClassList("tt-ai-entity-row");

                    var toggle = new Toggle { value = true };
                    toggle.style.marginRight = 4;

                    if (mandatory)
                    {
                        _excludedTypeIds.Remove(id);
                        toggle.SetEnabled(false);
                        toggle.tooltip = "Required — levels need at least one movable pin type.";
                    }
                    else
                    {
                        toggle.value = !_excludedTypeIds.Contains(id);
                        toggle.RegisterValueChangedCallback(e =>
                        {
                            if (e.newValue) _excludedTypeIds.Remove(id);
                            else            _excludedTypeIds.Add(id);
                        });
                    }

                    item.Add(toggle);
                    var lbl = new Label(def.DisplayName);
                    if (mandatory) lbl.AddToClassList("tt-ai-entity-required");
                    item.Add(lbl);
                    _entitiesContainer.Add(item);
                }
            }
        }

        // ── Prompt ────────────────────────────────────────────────────────────

        private VisualElement BuildPromptSection()
        {
            var s = MakeSection("1 · Copy prompt");
            var hint = new Label("Build a self-contained prompt and copy it to your clipboard. Paste it into any AI chat (Claude, ChatGPT, Gemini …).");
            hint.AddToClassList("tt-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);
            s.Add(Btn("Copy prompt to clipboard", CopyPrompt, "tt-btn--primary"));
            return s;
        }

        // ── Import ────────────────────────────────────────────────────────────

        private VisualElement BuildImportSection()
        {
            var s = MakeSection("2 · Import JSON");
            var hint = new Label("Paste the AI's JSON response below, then click Import. The level loads into the Level Creator for review — validate and solve before saving.");
            hint.AddToClassList("tt-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);

            _jsonField = new TextField { multiline = true };
            _jsonField.value = EditorPrefs.GetString(P("Json"), string.Empty);
            _jsonField.style.minHeight   = 160;
            _jsonField.style.marginBottom = 6;
            s.Add(_jsonField);

            s.Add(Btn("Import JSON → Level Creator", ImportJson, "tt-btn--save"));

            _statusLabel = new Label(" ");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 6;
            s.Add(_statusLabel);

            return s;
        }

        // ── Reset footer (always visible, outside scroll) ─────────────────────

        private VisualElement BuildResetFooter()
        {
            var footer = new VisualElement();
            footer.style.flexShrink        = 0;
            footer.style.flexDirection     = FlexDirection.Row;
            footer.style.alignItems        = Align.Center;
            footer.style.justifyContent    = Justify.SpaceBetween;
            footer.style.paddingLeft       = 10;
            footer.style.paddingRight      = 10;
            footer.style.paddingTop        = 6;
            footer.style.paddingBottom     = 6;
            footer.style.borderTopWidth    = 1;
            footer.style.borderTopColor    = new Color(1f, 1f, 1f, 0.1f);
            footer.style.backgroundColor   = new Color(0.13f, 0.13f, 0.13f, 1f);

            var hint = new Label("Reset all fields to defaults");
            hint.style.fontSize  = 11;
            hint.style.color     = new Color(1f, 1f, 1f, 0.45f);
            footer.Add(hint);

            var btn = Btn("Reset", ResetPrefs, "tt-btn--danger");
            btn.tooltip = "Clears all saved state and resets every field to its default value.";
            footer.Add(btn);

            return footer;
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void LoadReference()
        {
            int id = _refLevelId?.value ?? 0;
            if (id <= 0) { _refLevel = null; if (_refLabel != null) _refLabel.text = "No reference — AI generates freely."; return; }
            var asset = LevelSaveUtility.GetSelectedLevel(id, LevelEditorPaths.Levels);
            if (asset == null) { _refLevel = null; if (_refLabel != null) _refLabel.text = $"Level {id} not found."; return; }
            _refLevel = asset;
            if (_refLabel != null) _refLabel.text = $"Reference: Level {id}  ·  {asset.GridWidth}x{asset.GridHeight}  ·  {asset.Ropes.Count} rope(s)";
        }

        private void CopyPrompt()
        {
            EditorGUIUtility.systemCopyBuffer = LevelAiGenerator.BuildManualPrompt(BuildRequest());
            SetStatus("✓ Prompt copied — paste into your AI chat, then paste the JSON answer back here.", ok: true);
        }

        private void ImportJson()
        {
            if (LevelAiGenerator.TryParseLevelJson(_jsonField?.value ?? string.Empty, out var level, out var error))
            {
                var creator = GetWindow<LevelCreator>();
                creator.LoadGeneratedLevel(level);
                creator.Focus();
                SetStatus("✓ Imported into Level Creator — validate and solve before saving.", ok: true);
            }
            else
            {
                SetStatus("✗ " + error, ok: false);
            }
        }

        private void SetStatus(string msg, bool ok)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = msg;
            _statusLabel.EnableInClassList("tt-validation__ok",    ok);
            _statusLabel.EnableInClassList("tt-validation__error", !ok);
        }

        private LevelGenerationRequest BuildRequest() => new()
        {
            GridWidth   = Mathf.Max(1, _gridWidth?.value    ?? 6),
            GridHeight  = Mathf.Max(1, _gridHeight?.value   ?? 6),
            TimeSeconds = Mathf.Max(1, _timeSeconds?.value  ?? 45),
            Difficulty  = _difficulty?.value ?? "Medium",
            EntityTypeIds  = _entityDefs.Where(d => !_excludedTypeIds.Contains(d.TypeId)).Select(d => d.TypeId).ToList(),
            NailedTypeIds  = _entityDefs.Where(d => LevelCreator.IsNailed(d) && !_excludedTypeIds.Contains(d.TypeId)).Select(d => d.TypeId).ToList(),
            PaletteHex     = _swatches.Select(sw => "#" + ColorUtility.ToHtmlStringRGB(sw.color)).ToList(),
            ReferenceLevelDescription = _refLevel != null ? LevelAiGenerator.DescribeLevel(_refLevel) : null,
        };

        private void Refresh()
        {
            _baseTypes.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityBaseTypeSO)}"))
            {
                var b = AssetDatabase.LoadAssetAtPath<EntityBaseTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (b != null) _baseTypes.Add(b);
            }
            _entityDefs.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityDefinitionSO)}"))
            {
                var d = AssetDatabase.LoadAssetAtPath<EntityDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null) _entityDefs.Add(d);
            }
            _swatches.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(ColorPaletteSO)}"))
            {
                var pal = AssetDatabase.LoadAssetAtPath<ColorPaletteSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (pal == null) continue;
                foreach (var e in pal.Entries) _swatches.Add((e.Name, e.Color));
            }
        }

        private void RefreshAndRebuild() { Refresh(); RebuildEntities(); }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static VisualElement MakeSection(string title)
        {
            var f = new Foldout { text = title, value = true };
            f.AddToClassList("tt-section");
            return f;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement(); r.AddToClassList("tt-row"); return r;
        }

        private static Button Btn(string text, System.Action onClick, string cls)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("tt-btn");
            if (!string.IsNullOrEmpty(cls)) b.AddToClassList(cls);
            return b;
        }

        private static IntegerField CompactInt(string label, int val)
        {
            var f = new IntegerField(label) { value = val };
            f.AddToClassList("tt-num");
            return f;
        }
    }
}
