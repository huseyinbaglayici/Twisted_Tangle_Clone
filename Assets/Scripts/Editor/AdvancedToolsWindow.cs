using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    public class AdvancedToolsWindow : EditorWindow
    {
        private VisualElement _batchResultsContainer;
        private Label _batchSummary;
        private IntegerField _rangeFrom, _rangeTo;

        [MenuItem("TwistedTangle/Advanced Tools")]
        public static void ShowWindow()
        {
            var w = GetWindow<AdvancedToolsWindow>();
            w.titleContent = new GUIContent("Tangle — Advanced Tools");
            w.minSize = new Vector2(500, 280);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("tt-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("tt-main-scroll");

            var title = new Label("Advanced Tools");
            title.AddToClassList("tt-title");
            scroll.Add(title);

            scroll.Add(BuildBatchCheckSection());

            root.Add(scroll);
        }

        private VisualElement BuildBatchCheckSection()
        {
            var section = new Foldout { text = "Batch Level Check", value = true };
            section.AddToClassList("tt-section");

            var controls = new VisualElement();
            controls.AddToClassList("tt-row");

            _rangeFrom = new IntegerField("From") { value = 1 };
            _rangeFrom.AddToClassList("tt-num");
            _rangeTo = new IntegerField("To") { value = 999 };
            _rangeTo.AddToClassList("tt-num");
            controls.Add(_rangeFrom);
            controls.Add(_rangeTo);

            var runBtn = new Button(RunBatchCheck) { text = "Check" };
            runBtn.AddToClassList("tt-btn");
            runBtn.AddToClassList("tt-btn--primary");
            controls.Add(runBtn);

            _batchSummary = new Label();
            _batchSummary.AddToClassList("tt-metric");
            controls.Add(_batchSummary);

            section.Add(controls);

            _batchResultsContainer = new VisualElement();
            _batchResultsContainer.style.marginTop = 6;
            section.Add(_batchResultsContainer);

            return section;
        }

        private void RunBatchCheck()
        {
            _batchResultsContainer.Clear();
            _batchSummary.text = "Checking…";

            int from = Mathf.Min(_rangeFrom.value, _rangeTo.value);
            int to   = Mathf.Max(_rangeFrom.value, _rangeTo.value);

            var all = LevelBatchChecker.CheckAll();
            var results = all.Where(r => r.LevelId >= from && r.LevelId <= to).ToList();

            if (results.Count == 0)
            {
                _batchSummary.text = all.Count == 0
                    ? "No levels found in: " + LevelEditorPaths.Levels
                    : $"No levels in range {from}–{to}.";
                return;
            }

            int ok = 0;
            foreach (var r in results) if (r.ValidationErrors == 0 && r.Crossings == 0) ok++;
            _batchSummary.text = $"{ok}/{results.Count} valid & untangled ({from}–{to})";

            _batchResultsContainer.Add(MakeTableHeader());
            foreach (var r in results)
                _batchResultsContainer.Add(MakeTableRow(r));
        }

        private static VisualElement MakeTableHeader()
        {
            var row = MakeRow();
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1, 1, 1, 0.15f);
            row.style.paddingBottom = 3;
            row.style.marginBottom = 2;

            foreach (var (label, w) in Headers())
            {
                var cell = new Label(label);
                cell.style.width = w;
                cell.style.color = new Color(0.85f, 0.85f, 0.85f);
                cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(cell);
            }
            return row;
        }

        private static VisualElement MakeTableRow(LevelCheckResult r)
        {
            string status = r.ValidationErrors > 0 ? "INVALID"
                : r.Crossings == 0                 ? "Clean"
                : "Has crossings";

            string statusCls = r.ValidationErrors > 0 ? "tt-validation__error"
                : r.Crossings == 0                    ? "tt-validation__ok"
                : "tt-validation__warn";

            string errors = r.ValidationErrors > 0 ? r.ValidationErrors.ToString() : "";

            string[] values = {
                r.LevelId.ToString(), status,
                r.Crossings.ToString(), errors
            };

            var row = MakeRow();
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;

            var widths = ColWidths();
            for (int i = 0; i < values.Length; i++)
            {
                var cell = new Label(values[i]);
                cell.style.width = widths[i];
                cell.style.color = new Color(0.72f, 0.72f, 0.72f);
                if (i == 1) cell.AddToClassList(statusCls);
                row.Add(cell);
            }
            return row;
        }

        private static VisualElement MakeRow()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        private static (string label, int width)[] Headers() => new[]
        {
            ("ID",         44),
            ("Status",    120),
            ("Crossings",  80),
            ("Errors",     60),
        };

        private static int[] ColWidths()
        {
            var h = Headers();
            var w = new int[h.Length];
            for (int i = 0; i < h.Length; i++) w[i] = h[i].width;
            return w;
        }
    }
}
