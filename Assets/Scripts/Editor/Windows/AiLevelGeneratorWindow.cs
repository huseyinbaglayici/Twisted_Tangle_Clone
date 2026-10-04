using System.Collections.Generic;
using Runtime.Level;
using TwistedTangle.Editor.Generation;
using TwistedTangle.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Windows
{
    public class AiLevelGeneratorWindow : EditorWindow
    {
        private static string P(string key) => $"TwistedTangle.AiGen.{PlayerSettings.productGUID}.{key}";

        private static readonly List<string> DifficultyChoices = new() { "Easy", "Medium", "Hard" };

        private IntegerField _mapId, _moveLimit, _refLevelId;
        private DropdownField _difficulty;
        private Label _refLabel, _statusLabel;
        private TextField _jsonField;
        private LevelJson _refLevel;

        [MenuItem("TwistedTangle/AI Level Generator")]
        public static void ShowWindow()
        {
            var w = GetWindow<AiLevelGeneratorWindow>();
            w.titleContent = new GUIContent("AI Level Generator");
            w.minSize = new Vector2(540, 520);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList(Css.Root);

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = EditorColors.WindowBg;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.RightScroll);
            scroll.style.flexGrow = 1;
            scroll.style.paddingTop = 8;
            scroll.style.paddingBottom = 8;
            scroll.style.paddingLeft = 10;
            scroll.style.paddingRight = 10;

            scroll.Add(BuildSettingsSection());
            scroll.Add(BuildPromptSection());
            scroll.Add(BuildImportSection());

            root.Add(scroll);
            root.Add(BuildResetFooter());
        }

        private void OnDisable() => SavePrefs();

        private void SavePrefs()
        {
            EditorPrefs.SetInt(P("Map"), _mapId?.value ?? 1);
            EditorPrefs.SetInt(P("Moves"), _moveLimit?.value ?? 0);
            EditorPrefs.SetString(P("Diff"), _difficulty?.value ?? "Easy");
            EditorPrefs.SetInt(P("RefId"), _refLevelId?.value ?? 0);
            EditorPrefs.SetString(P("Json"), _jsonField?.value ?? string.Empty);
        }

        private void ResetPrefs()
        {
            foreach (var key in new[] { "Map", "Moves", "Diff", "RefId", "Json" })
                EditorPrefs.DeleteKey(P(key));

            _mapId?.SetValueWithoutNotify(1);
            _moveLimit?.SetValueWithoutNotify(0);
            _difficulty?.SetValueWithoutNotify("Easy");
            _refLevelId?.SetValueWithoutNotify(0);
            _jsonField?.SetValueWithoutNotify(string.Empty);
            _refLevel = null;
            if (_refLabel != null) _refLabel.text = "No reference — AI generates freely.";
            if (_statusLabel != null) _statusLabel.text = " ";
        }

        private VisualElement BuildSettingsSection()
        {
            var s = MakeSection("Generation settings");

            var row = MakeRow();
            row.AddToClassList(Css.RowWrap);
            _mapId = CompactInt("Map", EditorPrefs.GetInt(P("Map"), 1));
            _mapId.tooltip = "Map id: grid = (3+N) × (5+N).";
            _moveLimit = CompactInt("Moves", EditorPrefs.GetInt(P("Moves"), 0));
            _moveLimit.tooltip = "Move limit (0 = unlimited).";
            row.Add(_mapId);
            row.Add(_moveLimit);
            s.Add(row);

            var diffRow = MakeRow();
            string savedDiff = EditorPrefs.GetString(P("Diff"), "Easy");
            _difficulty = new DropdownField("Difficulty", DifficultyChoices, Mathf.Max(0, DifficultyChoices.IndexOf(savedDiff)));
            _difficulty.labelElement.style.minWidth = 0;
            _difficulty.labelElement.style.width = StyleKeyword.Auto;
            _difficulty.style.flexShrink = 0;
            diffRow.Add(_difficulty);
            s.Add(diffRow);

            var refRow = MakeRow();
            refRow.AddToClassList(Css.RowWrap);
            _refLevelId = CompactInt("Ref level", EditorPrefs.GetInt(P("RefId"), 0));
            _refLevelId.tooltip = "Optional: enter a level number and click Load. The AI will mimic its style.";
            refRow.Add(_refLevelId);
            refRow.Add(Btn("Load", LoadReference, null));
            s.Add(refRow);

            _refLabel = new Label("No reference — AI generates freely.");
            _refLabel.AddToClassList(Css.Hint);
            s.Add(_refLabel);
            return s;
        }

        private VisualElement BuildPromptSection()
        {
            var s = MakeSection("1 · Copy prompt");
            var hint = new Label(
                "Build a self-contained prompt and copy it to your clipboard. Paste it into any AI chat (Claude, ChatGPT, Gemini …).");
            hint.AddToClassList(Css.Hint);
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);
            s.Add(Btn("Copy prompt to clipboard", CopyPrompt, Css.BtnPrimary));
            return s;
        }

        private VisualElement BuildImportSection()
        {
            var s = MakeSection("2 · Import JSON");
            var hint = new Label(
                "Paste the AI's JSON response below, then click Import. The level loads into the Level Creator for review — validate before saving.");
            hint.AddToClassList(Css.Hint);
            hint.style.whiteSpace = WhiteSpace.Normal;
            s.Add(hint);

            _jsonField = new TextField { multiline = true };
            _jsonField.value = EditorPrefs.GetString(P("Json"), string.Empty);
            _jsonField.style.minHeight = 160;
            _jsonField.style.marginBottom = 6;
            s.Add(_jsonField);

            s.Add(Btn("Import JSON → Level Creator", ImportJson, Css.BtnSave));

            _statusLabel = new Label(" ");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 6;
            s.Add(_statusLabel);
            return s;
        }

        private VisualElement BuildResetFooter()
        {
            var footer = new VisualElement();
            footer.style.flexShrink = 0;
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.paddingLeft = 10;
            footer.style.paddingRight = 10;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 6;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = EditorColors.FooterBorder;
            footer.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f);

            var hint = new Label("Reset all fields to defaults");
            hint.style.fontSize = 11;
            hint.style.color = EditorColors.HintText;
            footer.Add(hint);

            var btn = Btn("Reset", ResetPrefs, Css.BtnDanger);
            btn.tooltip = "Clears all saved state and resets every field to its default value.";
            footer.Add(btn);
            return footer;
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void LoadReference()
        {
            int id = _refLevelId?.value ?? 0;
            _refLevel = id > 0 ? LevelFileUtility.Load(id) : null;
            if (_refLabel == null) return;
            if (id <= 0) _refLabel.text = "No reference — AI generates freely.";
            else if (_refLevel?.stages == null || _refLevel.stages.Count == 0) _refLabel.text = $"Level {id} not found.";
            else
                _refLabel.text = $"Reference: Level {id}  ·  map {_refLevel.stages[0].mapId}  ·  " +
                                 $"{_refLevel.stages[0].ropes.Count} rope(s)";
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
                SetStatus("✓ Imported into Level Creator — validate before saving.", ok: true);
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
            _statusLabel.EnableInClassList(Css.ValidationOk, ok);
            _statusLabel.EnableInClassList(Css.ValidationError, !ok);
        }

        private LevelGenerationRequest BuildRequest()
        {
            int paletteCount = 8;
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(RopePaletteSO)}"))
            {
                var palette = AssetDatabase.LoadAssetAtPath<RopePaletteSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (palette == null) continue;
                paletteCount = palette.Count;
                break;
            }

            return new LevelGenerationRequest
            {
                MapId = Mathf.Max(1, _mapId?.value ?? 1),
                MoveLimit = Mathf.Max(0, _moveLimit?.value ?? 0),
                Difficulty = _difficulty?.value ?? "Easy",
                PaletteCount = paletteCount,
                ReferenceLevelDescription = _refLevel != null ? LevelAiGenerator.DescribeLevel(_refLevel) : null,
            };
        }

        private static VisualElement MakeSection(string title)
        {
            var f = new Foldout { text = title, value = true };
            f.AddToClassList(Css.Section);
            return f;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList(Css.Row);
            return r;
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
