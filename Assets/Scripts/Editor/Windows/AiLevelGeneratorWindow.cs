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

        private const string AutoExamples = "Auto (2 from difficulty)";
        private const string NoExamples = "None";

        private IntegerField _mapId, _timeLimit;
        private DropdownField _difficulty, _example;
        private Label _profileLabel, _statusLabel;
        private TextField _jsonField;

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
            EditorPrefs.SetInt(P("Time"), _timeLimit?.value ?? 0);
            EditorPrefs.SetString(P("Diff"), _difficulty?.value ?? "Easy");
            EditorPrefs.SetString(P("Example"), _example?.value ?? AutoExamples);
            EditorPrefs.SetString(P("Json"), _jsonField?.value ?? string.Empty);
        }

        private void ResetPrefs()
        {
            foreach (var key in new[] { "Map", "Time", "Diff", "Example", "Json" })
                EditorPrefs.DeleteKey(P(key));

            _mapId?.SetValueWithoutNotify(1);
            _timeLimit?.SetValueWithoutNotify(0);
            _difficulty?.SetValueWithoutNotify("Easy");
            _example?.SetValueWithoutNotify(AutoExamples);
            _jsonField?.SetValueWithoutNotify(string.Empty);
            UpdateProfileLabel();
            if (_statusLabel != null) _statusLabel.text = " ";
        }

        private VisualElement BuildSettingsSection()
        {
            var s = MakeSection("Generation settings");

            var row = MakeRow();
            row.AddToClassList(Css.RowWrap);
            _mapId = CompactInt("Map", EditorPrefs.GetInt(P("Map"), 1));
            _mapId.tooltip = "Map id: grid = (3+N) × (5+N).";
            _timeLimit = CompactInt("Time(s)", EditorPrefs.GetInt(P("Time"), 0));
            _timeLimit.tooltip = "Time limit in seconds (0 = median of the shipped levels of this difficulty).";
            row.Add(_mapId);
            row.Add(_timeLimit);
            s.Add(row);

            var diffRow = MakeRow();
            string savedDiff = EditorPrefs.GetString(P("Diff"), "Easy");
            _difficulty = new DropdownField("Difficulty", DifficultyChoices, Mathf.Max(0, DifficultyChoices.IndexOf(savedDiff)));
            _difficulty.labelElement.style.minWidth = 0;
            _difficulty.labelElement.style.width = StyleKeyword.Auto;
            _difficulty.style.flexShrink = 0;
            _difficulty.RegisterValueChangedCallback(_ => UpdateProfileLabel());
            diffRow.Add(_difficulty);
            s.Add(diffRow);

            var choices = new List<string> { AutoExamples, NoExamples };
            foreach (int n in LevelFileUtility.ListLevelNumbers()) choices.Add($"Level {n}");
            string savedExample = EditorPrefs.GetString(P("Example"), AutoExamples);
            var exampleRow = MakeRow();
            _example = new DropdownField("Examples", choices, Mathf.Max(0, choices.IndexOf(savedExample)));
            _example.tooltip = "Shipped levels added to the prompt as examples of the output format and style.";
            _example.labelElement.style.minWidth = 0;
            _example.labelElement.style.width = StyleKeyword.Auto;
            exampleRow.Add(_example);
            s.Add(exampleRow);

            _profileLabel = new Label();
            _profileLabel.AddToClassList(Css.Hint);
            _profileLabel.style.whiteSpace = WhiteSpace.Normal;
            s.Add(_profileLabel);
            UpdateProfileLabel();
            return s;
        }

        private VisualElement BuildPromptSection()
        {
            var s = MakeSection("1 · Copy prompt");
            var hint = new Label(
                "Build a self-contained prompt and copy it to your clipboard. Paste it into any AI chat.");
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
                "Paste the AI's JSON response below, then click Import. Rope paths are built from pins, bend and over/under; the level opens in the Level Creator — validate, then save.");
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

        private void UpdateProfileLabel()
        {
            if (_profileLabel == null) return;
            var profile = LevelDataStats.Profile(_difficulty?.value ?? "Easy");
            _profileLabel.text = profile.Levels.Count > 0 ? profile.Summary : "No shipped levels in this range.";
        }

        private List<LevelJson> PickExamples(string difficulty)
        {
            string choice = _example?.value ?? AutoExamples;
            if (choice == NoExamples) return new List<LevelJson>();
            if (choice == AutoExamples)
                return LevelAiGenerator.PickExamples(difficulty, 2, System.Environment.TickCount);
            var level = int.TryParse(choice.Replace("Level ", ""), out int n) ? LevelFileUtility.Load(n) : null;
            return level != null ? new List<LevelJson> { level } : new List<LevelJson>();
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
            var palette = LevelFileUtility.FindPalette();
            string difficulty = _difficulty?.value ?? "Easy";
            return new LevelGenerationRequest
            {
                MapId = Mathf.Max(1, _mapId?.value ?? 1),
                TimeLimitSeconds = Mathf.Max(0, _timeLimit?.value ?? 0),
                Difficulty = difficulty,
                PaletteCount = palette != null && palette.Count > 0 ? palette.Count : 8,
                Examples = PickExamples(difficulty),
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
