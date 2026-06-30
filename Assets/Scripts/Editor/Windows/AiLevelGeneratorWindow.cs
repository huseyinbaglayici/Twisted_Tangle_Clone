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
    public class AiLevelGeneratorWindow : EditorWindow
    {
        private static string P(string key) => $"TwistedTangle.AiGen.{PlayerSettings.productGUID}.{key}";

        private readonly List<EntityBaseTypeSO>           _baseTypes  = new();
        private readonly List<EntityDefinitionSO>         _entityDefs = new();
        private readonly List<(string name, Color color)> _swatches   = new();
        private readonly HashSet<string> _excludedTypeIds = new();

        private IntegerField  _gridWidth, _gridHeight, _timeSeconds, _refLevelId;
        private DropdownField _difficulty;
        private Label         _refLabel, _statusLabel, _diffHintLabel;
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
            root.AddToClassList(Css.Root);

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = EditorColors.WindowBg;

            Refresh();
            LoadPrefs(); // restore field values + excluded set before building UI

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.RightScroll);
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


        private void LoadPrefs()
        {
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

            if (_gridWidth   != null) _gridWidth.value   = 6;
            if (_gridHeight  != null) _gridHeight.value  = 6;
            if (_timeSeconds != null) _timeSeconds.value = 45;
            if (_difficulty  != null) { _difficulty.value = "Medium"; UpdateDiffHint(); }
            if (_refLevelId  != null) _refLevelId.value  = 0;
            if (_jsonField   != null) _jsonField.value   = string.Empty;
            _refLevel = null;
            if (_refLabel    != null) _refLabel.text = "No reference — AI generates freely.";
            if (_statusLabel != null) _statusLabel.text = " ";
            _excludedTypeIds.Clear();
            RebuildEntities();
        }


        private VisualElement BuildSettingsSection()
        {
            var s = MakeSection("Generation settings");

            var gridRow = MakeRow();
            gridRow.AddToClassList(Css.RowWrap);
            _gridWidth   = CompactInt("Width",    EditorPrefs.GetInt(P("W"),    6));
            _gridHeight  = CompactInt("Height",   EditorPrefs.GetInt(P("H"),    6));
            _timeSeconds = CompactInt("Time(s)",  EditorPrefs.GetInt(P("Time"), 45));
            gridRow.Add(_gridWidth);
            gridRow.Add(_gridHeight);
            gridRow.Add(_timeSeconds);
            s.Add(gridRow);

            var diffRow = MakeRow();
            string savedDiff = EditorPrefs.GetString(P("Diff"), "Normal");
            var choices = new List<string> { "Normal", "Hard", "VeryHard" };
            int diffIdx = Mathf.Max(0, choices.IndexOf(savedDiff));
            _difficulty = new DropdownField("Difficulty", choices, diffIdx);
            _difficulty.labelElement.style.minWidth = 0;
            _difficulty.labelElement.style.width    = StyleKeyword.Auto;
            _difficulty.style.flexShrink = 0;
            _difficulty.RegisterValueChangedCallback(_ => UpdateDiffHint());
            diffRow.Add(_difficulty);
            s.Add(diffRow);

            _diffHintLabel = new Label();
            _diffHintLabel.AddToClassList(Css.Hint);
            _diffHintLabel.style.whiteSpace = WhiteSpace.Normal;
            _diffHintLabel.style.marginBottom = 4;
            s.Add(_diffHintLabel);
            UpdateDiffHint();

            var refRow = MakeRow();
            refRow.AddToClassList(Css.RowWrap);
            _refLevelId = CompactInt("Ref level", EditorPrefs.GetInt(P("RefId"), 0));
            _refLevelId.Q<Label>().style.minWidth = 0;
            _refLevelId.Q<Label>().style.width    = StyleKeyword.Auto;
            _refLevelId.tooltip = "Optional: enter a level ID and click Load. The AI will mimic its style.";
            refRow.Add(_refLevelId);
            refRow.Add(Btn("Load", LoadReference, null));
            s.Add(refRow);

            _refLabel = new Label("No reference — AI generates freely.");
            _refLabel.AddToClassList(Css.Hint);
            s.Add(_refLabel);

                var sub = new Foldout { text = "Entity types", value = false };
            sub.AddToClassList(Css.Subgroup);
            sub.style.marginTop = 6;

            var refreshBtn = Btn("↻ Refresh / Fetch", RefreshAndRebuild, Css.Tool);
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
                Color  a = def.BaseType != null ? def.BaseType.EditorColor  : EditorColors.EntityFallback;
                if (!groups.ContainsKey(g)) { groups[g] = (a, new List<EntityDefinitionSO>()); order.Add(g); }
                groups[g].defs.Add(def);
            }

            foreach (string g in order)
            {
                var (accent, defs) = groups[g];
                var header = new Label(g);
                header.AddToClassList(Css.AiGroupHeader);
                header.style.borderLeftColor = accent;
                _entitiesContainer.Add(header);

                foreach (var def in defs.OrderBy(d => IsObstacle(d) ? 2 : LevelCreator.IsNailed(d) ? 1 : 0).ThenBy(d => d.DisplayName))
                {
                    string id   = def.TypeId;
                    bool mandatory = !IsObstacle(def) && !LevelCreator.IsNailed(def);

                    var item = MakeRow();
                    item.AddToClassList(Css.AiEntityRow);

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
                    string suffix = IsObstacle(def) ? " [obstacle]"
                        : LevelCreator.IsNailed(def) ? " [nailed]"
                        : string.Empty;
                    var lbl = new Label(def.DisplayName + suffix);
                    if (mandatory) lbl.AddToClassList(Css.AiEntityRequired);
                    item.Add(lbl);
                    _entitiesContainer.Add(item);
                }
            }
        }


        private VisualElement BuildPromptSection()
        {
            var s = MakeSection("1 · Copy prompt");
            var hint = new Label("Build a self-contained prompt and copy it to your clipboard. Paste it into any AI chat (Claude, ChatGPT, Gemini …).");
            hint.AddToClassList(Css.Hint);
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);
            s.Add(Btn("Copy prompt to clipboard", CopyPrompt, Css.BtnPrimary));
            return s;
        }


        private VisualElement BuildImportSection()
        {
            var s = MakeSection("2 · Import JSON");
            var hint = new Label("Paste the AI's JSON response below, then click Import. The level loads into the Level Creator for review — validate and solve before saving.");
            hint.AddToClassList(Css.Hint);
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);

            _jsonField = new TextField { multiline = true };
            _jsonField.value = EditorPrefs.GetString(P("Json"), string.Empty);
            _jsonField.style.minHeight   = 160;
            _jsonField.style.marginBottom = 6;
            s.Add(_jsonField);

            s.Add(Btn("Import JSON → Level Creator", ImportJson, Css.BtnSave));

            _statusLabel = new Label(" ");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 6;
            s.Add(_statusLabel);

            return s;
        }


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
            footer.style.borderTopColor    = EditorColors.FooterBorder;
            footer.style.backgroundColor   = new Color(0.13f, 0.13f, 0.13f, 1f);

            var hint = new Label("Reset all fields to defaults");
            hint.style.fontSize  = 11;
            hint.style.color     = EditorColors.HintText;
            footer.Add(hint);

            var btn = Btn("Reset", ResetPrefs, Css.BtnDanger);
            btn.tooltip = "Clears all saved state and resets every field to its default value.";
            footer.Add(btn);

            return footer;
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void UpdateDiffHint()
        {
            if (_diffHintLabel == null) return;
            _diffHintLabel.text = (_difficulty?.value ?? "Normal") switch
            {
                "Hard"     => "4–6 ropes · 5–9 crossings · 7–11 pegs · ≤6 moves · up to 3 nailed endpoints · hub pegs connecting multiple ropes",
                "VeryHard" => "5–7 ropes · 7–12 crossings · 9–13 pegs · ≤9 moves · up to 4 nailed endpoints · complex web topology",
                _          => "3–4 ropes · 3–6 crossings · 5–8 pegs · ≤4 moves · up to 1 nailed endpoint · 1–2 shared hub pegs",
            };
        }

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
            _statusLabel.EnableInClassList(Css.ValidationOk,    ok);
            _statusLabel.EnableInClassList(Css.ValidationError, !ok);
        }

        private static bool IsObstacle(EntityDefinitionSO d) => d.CanvasMarker != CanvasMarker.None;

        private LevelGenerationRequest BuildRequest()
        {
            var active = _entityDefs.Where(d => !_excludedTypeIds.Contains(d.TypeId)).ToList();
            return new LevelGenerationRequest
            {
                GridWidth   = Mathf.Max(1, _gridWidth?.value    ?? 6),
                GridHeight  = Mathf.Max(1, _gridHeight?.value   ?? 6),
                TimeSeconds = Mathf.Max(1, _timeSeconds?.value  ?? 45),
                Difficulty  = _difficulty?.value ?? "Medium",
                EntityTypeIds   = active.Where(d => !IsObstacle(d)).Select(d => d.TypeId).ToList(),
                NailedTypeIds   = active.Where(d => !IsObstacle(d) && LevelCreator.IsNailed(d)).Select(d => d.TypeId).ToList(),
                ObstacleTypeIds = active.Where(d => IsObstacle(d)).Select(d => d.TypeId).ToList(),
                PaletteHex      = _swatches.Select(sw => "#" + ColorUtility.ToHtmlStringRGB(sw.color)).ToList(),
                ReferenceLevelDescription = _refLevel != null ? LevelAiGenerator.DescribeLevel(_refLevel) : null,
            };
        }

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


        private static VisualElement MakeSection(string title)
        {
            var f = new Foldout { text = title, value = true };
            f.AddToClassList(Css.Section);
            return f;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement(); r.AddToClassList(Css.Row); return r;
        }

        private static Button Btn(string text, System.Action onClick, string cls)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList(Css.Btn);
            if (!string.IsNullOrEmpty(cls)) b.AddToClassList(cls);
            return b;
        }

        private static IntegerField CompactInt(string label, int val)
        {
            var f = new IntegerField(label) { value = val };
            f.AddToClassList(Css.Num);
            return f;
        }
    }
}
