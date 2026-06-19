using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Editor.Canvas;
using TwistedTangle.Editor.Input;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Editor.Validation;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using TwistedTangle.Editor.Geometry;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Twisted Tangle visual level editor. A designer places pegs on a grid, draws ropes between them,
    /// picks each rope's color from a palette, controls which rope sits on top at each crossing, and
    /// saves/loads/deletes levels by id — all without touching code. Peg types and palette colors are
    /// data-driven (PegDefinitionSO / ColorPaletteSO assets), so new content appears automatically.
    /// </summary>
    public class LevelCreator : EditorWindow
    {
        private enum Tool { Peg, Rope, Erase, Flip }

        private const string LevelsPath = "Assets/Resources/Data/Levels";
        private const string PegsPath = "Assets/Resources/Data/Pegs";
        private const string PalettesPath = "Assets/Resources/Data/Palettes";
        private const string UssPath = "Assets/Scripts/Editor/LevelCreator.uss";
        private const float FlipPickRadiusCells = 0.35f;

        // --- model ---
        private LevelDataSO _level;
        private int _currentLevelId = 0;
        private bool _isEditMode;
        private int _nextRopeId;

        // --- tool state ---
        private Tool _tool = Tool.Peg;
        private PegDefinitionSO _selectedPeg;
        private Color _ropeColor = new(0.90f, 0.20f, 0.20f);
        private RopeData _previewRope;
        private int _selectedRopeId = -1;

        // --- data-driven content ---
        private readonly List<PegDefinitionSO> _pegDefs = new();
        private readonly Dictionary<string, PegDefinitionSO> _pegLookup = new();
        private readonly List<(string name, Color color)> _swatches = new();

        // --- ui ---
        private IntegerField _levelIdField, _widthField, _heightField, _timeField;
        private RopeCanvasElement _canvas;
        private VisualElement _paletteContainer, _toolsContainer, _ropeListContainer, _validationContainer;
        private readonly Dictionary<Tool, Button> _toolButtons = new();

        // --- keyboard shortcuts ---
        private readonly Dictionary<string, System.Action> _commands = new();
        private static readonly Dictionary<Tool, string> ToolCommandIds = new()
        {
            { Tool.Peg, LevelEditorCommands.ToolPeg },
            { Tool.Rope, LevelEditorCommands.ToolRope },
            { Tool.Erase, LevelEditorCommands.ToolErase },
            { Tool.Flip, LevelEditorCommands.ToolFlip },
        };

        [MenuItem("TwistedTangle/Level Creation Tool")]
        public static void ShowWindow()
        {
            var w = GetWindow<LevelCreator>();
            w.titleContent = new GUIContent("Tangle Level Creator");
            w.minSize = new Vector2(560, 600);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("tt-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);

            RefreshPegDefinitions();
            RefreshPalettes();

            // Single outer scroll so the whole window scrolls when it's short — not just the grid.
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.AddToClassList("tt-main-scroll");

            scroll.Add(MakeTitle("Twisted Tangle — Level Creator"));
            scroll.Add(BuildLevelIoSection());
            scroll.Add(BuildGridSection());
            scroll.Add(BuildToolsSection());
            scroll.Add(BuildPaletteSection());
            scroll.Add(BuildRopeListSection());
            scroll.Add(BuildValidationSection());
            scroll.Add(BuildCanvasSection());

            root.Add(scroll);

            BuildCommandTable();
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnShortcutKeyDown);
            KeyBindingStore.Changed -= UpdateShortcutHints;
            KeyBindingStore.Changed += UpdateShortcutHints;

            RefreshAll();
            UpdateShortcutHints();
        }

        private void OnDisable() => KeyBindingStore.Changed -= UpdateShortcutHints;

        #region Data-driven discovery

        private void RefreshPegDefinitions()
        {
            _pegDefs.Clear();
            _pegLookup.Clear();

            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(PegDefinitionSO)}"))
            {
                var def = AssetDatabase.LoadAssetAtPath<PegDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                _pegDefs.Add(def);
                _pegLookup[def.TypeId] = def;
            }

            if (_selectedPeg == null || !_pegDefs.Contains(_selectedPeg))
                _selectedPeg = _pegDefs.FirstOrDefault();
        }

        private void RefreshPalettes()
        {
            _swatches.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(ColorPaletteSO)}"))
            {
                var pal = AssetDatabase.LoadAssetAtPath<ColorPaletteSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (pal == null) continue;
                foreach (var e in pal.Entries) _swatches.Add((e.Name, e.Color));
            }
        }

        private Color ResolvePegColor(string typeId) =>
            _pegLookup.TryGetValue(typeId, out var def) ? def.EditorColor : new Color(0.5f, 0.5f, 0.5f);

        /// <summary>Bootstraps a few example peg types so an empty project is usable immediately.</summary>
        private void CreateDefaultPegTypes()
        {
            EnsureFolder(PegsPath);
            CreatePegAsset("standard", "Standard", new Color(0.85f, 0.85f, 0.85f));
            CreatePegAsset("locked", "Locked", new Color(0.45f, 0.45f, 0.5f));
            CreatePegAsset("nailed", "Nailed", new Color(1f, 0.6f, 0.1f));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshPegDefinitions();
            RebuildPalette();
        }

        private static void CreatePegAsset(string typeId, string displayName, Color color)
        {
            string path = $"{PegsPath}/Peg_{displayName}.asset";
            if (AssetDatabase.LoadAssetAtPath<PegDefinitionSO>(path) != null) return;

            var so = CreateInstance<PegDefinitionSO>();
            var sObj = new SerializedObject(so);
            sObj.FindProperty("typeId").stringValue = typeId;
            sObj.FindProperty("displayName").stringValue = displayName;
            sObj.FindProperty("editorColor").colorValue = color;
            sObj.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(so, path);
        }

        /// <summary>Bootstraps a starter color palette so swatches exist out of the box.</summary>
        private void CreateDefaultPalette()
        {
            EnsureFolder(PalettesPath);
            string path = $"{PalettesPath}/DefaultPalette.asset";
            if (AssetDatabase.LoadAssetAtPath<ColorPaletteSO>(path) == null)
            {
                (string name, Color color)[] colors =
                {
                    ("Red", new Color(0.90f, 0.20f, 0.20f)),
                    ("Orange", new Color(1f, 0.55f, 0f)),
                    ("Yellow", new Color(0.95f, 0.85f, 0.10f)),
                    ("Green", new Color(0.30f, 0.75f, 0.35f)),
                    ("Blue", new Color(0.20f, 0.55f, 0.95f)),
                    ("Purple", new Color(0.55f, 0.25f, 0.80f)),
                    ("Pink", new Color(0.95f, 0.40f, 0.70f)),
                    ("White", Color.white)
                };

                var pal = CreateInstance<ColorPaletteSO>();
                var so = new SerializedObject(pal);
                var arr = so.FindProperty("entries");
                arr.arraySize = colors.Length;
                for (int i = 0; i < colors.Length; i++)
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("Name").stringValue = colors[i].name;
                    el.FindPropertyRelative("Color").colorValue = colors[i].color;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(pal, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            RefreshPalettes();
            RebuildPalette();
        }

        #endregion

        #region UI: static sections

        private VisualElement BuildLevelIoSection()
        {
            var s = MakeSection("Level (save / load / delete by id)");
            var row = MakeRow();

            _levelIdField = CompactIntField("Level Id", 1);
            row.Add(_levelIdField);
            row.Add(MakeButton("Load", () => LoadLevel(_levelIdField.value), "tt-btn--primary"));
            row.Add(MakeButton("Save", SaveCurrentLevel, "tt-btn--save"));
            row.Add(MakeButton("Delete", () => DeleteLevel(_levelIdField.value), "tt-btn--danger"));
            s.Add(row);
            return s;
        }

        private VisualElement BuildGridSection()
        {
            var s = MakeSection("Grid & time");
            var row = MakeRow();

            _widthField = CompactIntField("Width", 6);
            _heightField = CompactIntField("Height", 6);
            _timeField = CompactIntField("Time (s)", 45);
            row.Add(_widthField);
            row.Add(_heightField);
            row.Add(_timeField);
            row.Add(MakeButton("Generate Grid", GenerateGrid, "tt-btn--primary"));
            s.Add(row);
            return s;
        }

        private VisualElement BuildToolsSection()
        {
            var s = MakeSection("Tool");
            _toolsContainer = MakeRow();
            _toolsContainer.AddToClassList("tt-row--wrap");

            AddToolButton(Tool.Peg, "Peg");
            AddToolButton(Tool.Rope, "Rope");
            AddToolButton(Tool.Erase, "Erase Peg");
            AddToolButton(Tool.Flip, "Flip Crossing");

            s.Add(_toolsContainer);
            return s;
        }

        private void AddToolButton(Tool tool, string label)
        {
            var btn = new Button(() => SetTool(tool)) { text = label };
            btn.AddToClassList("tt-tool");
            _toolButtons[tool] = btn;
            _toolsContainer.Add(btn);
        }

        private VisualElement BuildPaletteSection()
        {
            var s = MakeSection("Brush — peg types & rope color");
            _paletteContainer = new VisualElement();
            s.Add(_paletteContainer);
            return s;
        }

        private VisualElement BuildRopeListSection()
        {
            var s = MakeSection("Ropes");
            _ropeListContainer = new VisualElement();
            s.Add(_ropeListContainer);
            return s;
        }

        private VisualElement BuildValidationSection()
        {
            var s = MakeSection("Validation & metrics");
            var row = MakeRow();
            row.Add(MakeButton("Validate", RebuildValidation, "tt-btn--primary"));
            s.Add(row);
            _validationContainer = new VisualElement();
            s.Add(_validationContainer);
            return s;
        }

        private VisualElement BuildCanvasSection()
        {
            // No inner scroll — the canvas has an explicit size and the outer scroll handles it.
            var host = new VisualElement();
            host.AddToClassList("tt-canvas-host");

            _canvas = new RopeCanvasElement { PegColorResolver = ResolvePegColor };
            _canvas.AddToClassList("tt-canvas");
            _canvas.CellClicked = OnCanvasCellClicked;
            _canvas.CellDragged = OnCanvasCellDragged;
            _canvas.Released = () => RefreshPanels();

            host.Add(_canvas);
            return host;
        }

        #endregion

        #region UI: dynamic panels

        private void RebuildPalette()
        {
            _paletteContainer.Clear();

            // --- peg type buttons (data-driven) ---
            if (_pegDefs.Count == 0)
            {
                _paletteContainer.Add(new HelpBox(
                    "No PegDefinitionSO assets found. Create peg types (Assets ▸ Create ▸ TwistedTangle ▸ " +
                    "Peg Definition) — they appear here automatically — or click below.",
                    HelpBoxMessageType.Info));
                _paletteContainer.Add(MakeButton("Create Default Peg Types", CreateDefaultPegTypes, "tt-btn--primary"));
            }
            else
            {
                var pegRow = MakeRow();
                pegRow.AddToClassList("tt-row--wrap");
                foreach (var def in _pegDefs)
                {
                    var btn = new Button(() => { _selectedPeg = def; SetTool(Tool.Peg); RebuildPalette(); })
                    {
                        text = def.DisplayName
                    };
                    btn.AddToClassList("tt-tool");
                    if (def == _selectedPeg) btn.AddToClassList("tt-tool--active");
                    btn.style.borderLeftWidth = 6;
                    btn.style.borderLeftColor = def.EditorColor;
                    pegRow.Add(btn);
                }
                _paletteContainer.Add(pegRow);
            }

            // --- rope color: free picker + palette swatches ---
            var colorRow = MakeRow();
            colorRow.AddToClassList("tt-row--wrap");
            var colorField = new UnityEditor.UIElements.ColorField("Rope color") { value = _ropeColor };
            colorField.style.minWidth = 170;
            colorField.RegisterValueChangedCallback(e =>
            {
                _ropeColor = e.newValue;
                if (_previewRope != null) _previewRope.Tint = _ropeColor;
                RefreshCanvas();
            });
            colorRow.Add(colorField);
            _paletteContainer.Add(colorRow);

            if (_swatches.Count > 0)
            {
                var swRow = MakeRow();
                swRow.AddToClassList("tt-row--wrap");
                foreach (var (name, color) in _swatches)
                {
                    var b = new Button(() =>
                    {
                        _ropeColor = color;
                        colorField.value = color;
                        if (_previewRope != null) _previewRope.Tint = color;
                        RefreshCanvas();
                    }) { tooltip = name };
                    b.AddToClassList("tt-swatch");
                    b.style.backgroundColor = color;
                    swRow.Add(b);
                }
                _paletteContainer.Add(swRow);
            }
            else
            {
                _paletteContainer.Add(MakeButton("Create Default Palette", CreateDefaultPalette, "tt-btn--primary"));
            }

            // --- rope authoring actions ---
            var actionRow = MakeRow();
            actionRow.Add(MakeButton("Finish Rope", FinishRope, "tt-btn--save"));
            actionRow.Add(MakeButton("Cancel Rope", CancelRope, "tt-btn--danger"));
            _paletteContainer.Add(actionRow);
        }

        private void RebuildRopeList()
        {
            _ropeListContainer.Clear();
            if (_level == null || _level.Ropes.Count == 0)
            {
                _ropeListContainer.Add(new Label("No ropes yet. Pick the Rope tool and click pegs in order."));
                return;
            }

            foreach (var rope in _level.Ropes.OrderByDescending(r => r.Layer))
            {
                var captured = rope;
                var row = MakeRow();

                var swatch = new VisualElement();
                swatch.AddToClassList("tt-peg-swatch");
                swatch.style.backgroundColor = rope.Tint;
                row.Add(swatch);

                var label = new Label($"Rope {rope.RopeId}  ·  L{rope.Layer}  ·  {rope.Path.Count} pts")
                {
                    style = { minWidth = 170, unityFontStyleAndWeight =
                        rope.RopeId == _selectedRopeId ? FontStyle.Bold : FontStyle.Normal }
                };
                row.Add(label);

                row.Add(MakeButton("Select", () => { _selectedRopeId = captured.RopeId; RefreshAll(); }, "tt-tool"));
                row.Add(MakeButton("▲ Front", () => { BringToFront(captured); RefreshAll(); }, "tt-tool"));
                row.Add(MakeButton("▼ Back", () => { SendToBack(captured); RefreshAll(); }, "tt-tool"));
                row.Add(MakeButton("✕", () => { DeleteRope(captured); RefreshAll(); }, "tt-btn--danger"));

                _ropeListContainer.Add(row);
            }
        }

        private void RebuildValidation()
        {
            _validationContainer.Clear();
            if (_level == null)
            {
                _validationContainer.Add(new Label("Generate or load a level first."));
                return;
            }

            var report = LevelValidator.Validate(_level, _pegLookup.Keys);

            var status = new Label(report.IsValid ? "✓ Level is valid" : $"✗ {report.Errors.Count} error(s)");
            status.AddToClassList(report.IsValid ? "tt-validation__ok" : "tt-validation__error");
            _validationContainer.Add(status);

            foreach (var err in report.Errors)
            {
                var l = new Label("• " + err);
                l.AddToClassList("tt-validation__error");
                _validationContainer.Add(l);
            }
            foreach (var warn in report.Warnings)
            {
                var l = new Label("• " + warn);
                l.AddToClassList("tt-validation__warn");
                _validationContainer.Add(l);
            }

            var m = report.Metrics;
            var metricsRow = MakeRow();
            metricsRow.AddToClassList("tt-row--wrap");
            AddMetric(metricsRow, $"Pegs: {m.PegCount}");
            AddMetric(metricsRow, $"Ropes: {m.RopeCount}");
            AddMetric(metricsRow, $"Crossings: {m.CrossingCount}");
            AddMetric(metricsRow, $"Colors: {m.ColorCount}");
            AddMetric(metricsRow, $"Overrides: {m.OverrideCount}");
            AddMetric(metricsRow, $"Length: {m.TotalPathLength:0.0}");
            AddMetric(metricsRow, $"Time: {_level.TimeSeconds}s");
            _validationContainer.Add(metricsRow);

            var diff = new Label($"Difficulty: {m.Difficulty} (score {m.DifficultyScore:0.0})");
            diff.AddToClassList($"tt-difficulty--{m.Difficulty}");
            _validationContainer.Add(diff);
        }

        private static void AddMetric(VisualElement row, string text)
        {
            var l = new Label(text);
            l.AddToClassList("tt-metric");
            row.Add(l);
        }

        #endregion

        #region Canvas interaction

        private void OnCanvasCellClicked(int x, int y, Vector2 local, int button)
        {
            if (_level == null) return;
            var coord = new Vector2Int(x, y);

            switch (_tool)
            {
                case Tool.Peg:
                    if (button == 1) RemovePeg(coord);
                    else PlacePeg(coord);
                    break;
                case Tool.Erase:
                    RemovePeg(coord);
                    break;
                case Tool.Rope:
                    if (button == 1) FinishRope();
                    else AddRopeWaypoint(coord);
                    break;
                case Tool.Flip:
                    FlipNearestCrossing(local);
                    break;
            }

            RefreshCanvas();
            if (_tool != Tool.Peg && _tool != Tool.Erase) RefreshPanels();
        }

        private void OnCanvasCellDragged(int x, int y)
        {
            if (_level == null) return;
            var coord = new Vector2Int(x, y);
            if (_tool == Tool.Peg) PlacePeg(coord);
            else if (_tool == Tool.Erase) RemovePeg(coord);
            else return;
            RefreshCanvas();
        }

        #endregion

        #region Model mutations

        private void GenerateGrid()
        {
            int w = Mathf.Max(1, _widthField.value);
            int h = Mathf.Max(1, _heightField.value);

            _level = CreateInstance<LevelDataSO>();
            _level.LevelId = _levelIdField.value;
            _level.GridWidth = w;
            _level.GridHeight = h;
            _level.TimeSeconds = _timeField.value;

            _isEditMode = false;
            _currentLevelId = 0;
            _nextRopeId = 0;
            _selectedRopeId = -1;
            _previewRope = null;

            RefreshAll();
        }

        private void PlacePeg(Vector2Int coord)
        {
            if (_selectedPeg == null) return;
            int idx = _level.Pegs.FindIndex(p => p.Coordinates == coord);
            if (idx >= 0) _level.Pegs[idx] = new PegData(coord, _selectedPeg.TypeId);
            else _level.Pegs.Add(new PegData(coord, _selectedPeg.TypeId));
        }

        private void RemovePeg(Vector2Int coord) =>
            _level.Pegs.RemoveAll(p => p.Coordinates == coord);

        private void AddRopeWaypoint(Vector2Int coord)
        {
            // Ropes connect pegs, so a waypoint must land on one.
            if (_level.Pegs.FindIndex(p => p.Coordinates == coord) < 0) return;

            if (_previewRope == null)
            {
                int layer = _level.Ropes.Count == 0 ? 0 : _level.Ropes.Max(r => r.Layer) + 1;
                _previewRope = new RopeData(_nextRopeId, _ropeColor, layer);
            }

            // Skip if same cell as the last waypoint (avoids zero-length segments).
            if (_previewRope.Path.Count > 0 && _previewRope.Path[^1].PegCoord == coord) return;
            _previewRope.Path.Add(new RopeWaypoint(coord));
        }

        private void FinishRope()
        {
            if (_previewRope == null) return;
            if (_previewRope.Path.Count >= 2)
            {
                _level.Ropes.Add(_previewRope);
                _selectedRopeId = _previewRope.RopeId;
                _nextRopeId++;
            }
            _previewRope = null;
            RefreshAll();
        }

        private void CancelRope()
        {
            _previewRope = null;
            RefreshAll();
        }

        private void DeleteRope(RopeData rope)
        {
            _level.Ropes.Remove(rope);
            _level.CrossingOverrides.RemoveAll(o => o.RopeIdA == rope.RopeId || o.RopeIdB == rope.RopeId);
            if (_selectedRopeId == rope.RopeId) _selectedRopeId = -1;
        }

        private void BringToFront(RopeData rope)
        {
            if (_level.Ropes.Count <= 1) return;
            rope.Layer = _level.Ropes.Max(r => r.Layer) + 1;
        }

        private void SendToBack(RopeData rope)
        {
            if (_level.Ropes.Count <= 1) return;
            rope.Layer = _level.Ropes.Min(r => r.Layer) - 1;
        }

        private void FlipNearestCrossing(Vector2 local)
        {
            var crossings = CrossingSolver.FindCrossings(_level.Ropes);
            if (crossings.Count == 0) return;

            float cell = _canvas.CellSize;
            var clickCs = new Vector2(local.x / cell, _level.GridHeight - local.y / cell);

            float best = FlipPickRadiusCells;
            bool found = false;
            RopeCrossing nearest = default;
            foreach (var c in crossings)
            {
                float d = Vector2.Distance(c.Point, clickCs);
                if (d <= best)
                {
                    best = d;
                    nearest = c;
                    found = true;
                }
            }
            if (!found) return;

            var key = CrossingOverride.Create(nearest.RopeIdA, nearest.SegA, nearest.RopeIdB, nearest.SegB);
            if (!_level.CrossingOverrides.Remove(key)) _level.CrossingOverrides.Add(key);
        }

        #endregion

        #region Save / load / delete

        private void SaveCurrentLevel()
        {
            if (_level == null)
            {
                EditorUtility.DisplayDialog("Save", "Generate or load a level first.", "OK");
                return;
            }

            _level.LevelId = _levelIdField.value;
            _level.TimeSeconds = _timeField.value;
            var report = LevelValidator.Validate(_level, _pegLookup.Keys);
            RebuildValidation();

            if (!report.IsValid)
            {
                EditorUtility.DisplayDialog("Cannot save — level has errors",
                    string.Join("\n", report.Errors.Take(10)), "OK");
                return;
            }

            var saved = LevelSaveUtility.SaveLevel(_level, LevelsPath);
            if (saved != null)
            {
                _isEditMode = true;
                _currentLevelId = _level.LevelId;
                EditorUtility.DisplayDialog("Saved", $"Level {_level.LevelId} saved.", "OK");
            }
        }

        private void LoadLevel(int id)
        {
            var asset = LevelSaveUtility.GetSelectedLevel(id, LevelsPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Load", $"No level with id {id}.", "OK");
                return;
            }

            // Work on an in-memory copy so edits don't dirty the asset until the next save.
            _level = CreateInstance<LevelDataSO>();
            LevelSaveUtility.CopyInto(asset, _level);

            _levelIdField.value = _level.LevelId;
            _widthField.value = _level.GridWidth;
            _heightField.value = _level.GridHeight;
            _timeField.value = _level.TimeSeconds;
            _isEditMode = true;
            _currentLevelId = _level.LevelId;
            _previewRope = null;
            _selectedRopeId = -1;
            _nextRopeId = _level.Ropes.Count == 0 ? 0 : _level.Ropes.Max(r => r.RopeId) + 1;

            RefreshAll();
        }

        private void DeleteLevel(int id)
        {
            if (!EditorUtility.DisplayDialog("Delete", $"Delete level {id}?", "Delete", "Cancel")) return;
            if (!LevelSaveUtility.DeleteSelectedLevel(id, LevelsPath))
            {
                EditorUtility.DisplayDialog("Delete", $"No level with id {id}.", "OK");
                return;
            }

            if (_isEditMode && _currentLevelId == id)
            {
                _level = null;
                _isEditMode = false;
                _currentLevelId = 0;
                RefreshAll();
            }
        }

        #endregion

        #region Refresh helpers

        private void SetTool(Tool tool)
        {
            _tool = tool;
            foreach (var kv in _toolButtons)
                kv.Value.EnableInClassList("tt-tool--active", kv.Key == tool);
            RefreshCanvas();
        }

        private void RefreshAll()
        {
            RefreshCanvas();
            RebuildPalette();
            RefreshPanels();
            foreach (var kv in _toolButtons)
                kv.Value.EnableInClassList("tt-tool--active", kv.Key == _tool);
        }

        private void RefreshPanels()
        {
            RebuildRopeList();
            RebuildValidation();
        }

        private void RefreshCanvas()
        {
            if (_canvas == null) return;
            _canvas.Level = _level;
            _canvas.GridWidth = _level?.GridWidth ?? 0;
            _canvas.GridHeight = _level?.GridHeight ?? 0;
            _canvas.PreviewRope = _previewRope;
            _canvas.SelectedRopeId = _selectedRopeId;
            _canvas.ShowCrossings = _tool == Tool.Flip;
            _canvas.Redraw();
        }

        #endregion

        #region Keyboard shortcuts

        /// <summary>Maps each bindable command id to the method that runs it. See <see cref="LevelEditorCommands"/>.</summary>
        private void BuildCommandTable()
        {
            _commands[LevelEditorCommands.ToolPeg] = () => SetTool(Tool.Peg);
            _commands[LevelEditorCommands.ToolRope] = () => SetTool(Tool.Rope);
            _commands[LevelEditorCommands.ToolErase] = () => SetTool(Tool.Erase);
            _commands[LevelEditorCommands.ToolFlip] = () => SetTool(Tool.Flip);

            _commands[LevelEditorCommands.Save] = SaveCurrentLevel;
            _commands[LevelEditorCommands.Load] = () => LoadLevel(_levelIdField.value);
            _commands[LevelEditorCommands.Delete] = () => DeleteLevel(_levelIdField.value);
            _commands[LevelEditorCommands.GenerateGrid] = GenerateGrid;

            _commands[LevelEditorCommands.FinishRope] = FinishRope;
            _commands[LevelEditorCommands.CancelRope] = CancelRope;
            _commands[LevelEditorCommands.RopeToFront] = BringSelectedRopeToFront;
            _commands[LevelEditorCommands.RopeToBack] = SendSelectedRopeToBack;
            _commands[LevelEditorCommands.RopeDelete] = DeleteSelectedRope;

            _commands[LevelEditorCommands.Validate] = RebuildValidation;
        }

        private void OnShortcutKeyDown(KeyDownEvent e)
        {
            var combo = KeyCombo.FromEvent(e);
            if (combo.IsEmpty) return;

            // Don't hijack plain (unmodified) keys while the user is typing in a numeric/text field.
            if (!combo.Ctrl && !combo.Alt && IsEditingText()) return;

            var id = KeyBindingStore.FindCommandFor(combo);
            if (id == null || !_commands.TryGetValue(id, out var action)) return;

            action.Invoke();
            e.StopPropagation();
        }

        private bool IsEditingText()
        {
            var focused = rootVisualElement.focusController?.focusedElement as VisualElement;
            for (var el = focused; el != null; el = el.parent)
                if (el is TextField || el is IntegerField || el is FloatField) return true;
            return false;
        }

        private RopeData SelectedRope() =>
            _level?.Ropes.FirstOrDefault(r => r.RopeId == _selectedRopeId);

        private void BringSelectedRopeToFront()
        {
            var rope = SelectedRope();
            if (rope == null) return;
            BringToFront(rope);
            RefreshAll();
        }

        private void SendSelectedRopeToBack()
        {
            var rope = SelectedRope();
            if (rope == null) return;
            SendToBack(rope);
            RefreshAll();
        }

        private void DeleteSelectedRope()
        {
            var rope = SelectedRope();
            if (rope == null) return;
            DeleteRope(rope);
            RefreshAll();
        }

        /// <summary>Shows each tool button's current shortcut in its tooltip; refreshes when bindings change.</summary>
        private void UpdateShortcutHints()
        {
            foreach (var kv in _toolButtons)
            {
                if (!ToolCommandIds.TryGetValue(kv.Key, out var id)) continue;
                var combo = KeyBindingStore.Get(id);
                kv.Value.tooltip = combo.IsEmpty ? string.Empty : $"Shortcut: {combo}";
            }
        }

        #endregion

        #region Small UI factory

        private static Label MakeTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("tt-title");
            return l;
        }

        private static VisualElement MakeSection(string header)
        {
            var s = new VisualElement();
            s.AddToClassList("tt-section");
            var h = new Label(header);
            h.AddToClassList("tt-section__header");
            s.Add(h);
            return s;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList("tt-row");
            return r;
        }

        private static Button MakeButton(string text, System.Action onClick, string ussClass)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("tt-btn");
            if (!string.IsNullOrEmpty(ussClass)) b.AddToClassList(ussClass);
            return b;
        }

        private static IntegerField CompactIntField(string label, int value)
        {
            var f = new IntegerField(label) { value = value };
            f.AddToClassList("tt-num");
            return f;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        #endregion
    }
}
