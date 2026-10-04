using System.Collections.Generic;
using System.Linq;
using Editor.Canvas;
using Editor.Input;
using Runtime.Level;
using TwistedTangle.Editor;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Windows
{
    /// <summary>
    /// Edits the level JSON files the runtime plays (Resources/Levels/level_NNN.json):
    /// pins on cells (+ locked), ropes pinA → pinB with colorIndex / layer / authored path, optional tutorial.
    /// </summary>
    public class LevelCreator : EditorWindow
    {
        private enum Tool
        {
            Pin,
            Rope,
            Lock,
            Erase,
            Tutorial
        }

        private static readonly List<string> DifficultyChoices = new() { "Easy", "Medium", "Hard" };

        private LevelEditSession _session;
        private int _loadedLevelNumber; // file the current level came from (0 = unsaved)

        private Tool _tool = Tool.Pin;
        private RopePaletteSO _palette;
        private int _colorIndex;
        private int _pendingPinId = -1;   // rope tool: first pin / tutorial tool: chosen pin
        private int _selectedRopeId = -1;
        private int _draggingPinId = -1;  // pin tool: pin being dragged to another cell
        private readonly Dictionary<int, (Vector2[] path, float length)> _dragBasePaths = new();

        private IntegerField _levelNumberField, _mapField, _moveLimitField, _timeLimitField;
        private DropdownField _difficultyField;
        private Label _gridSizeLabel, _tutorialLabel;
        private RopeCanvasElement _canvas;
        private VisualElement _canvasHost;
        private Label _zoomLabel;
        private float _zoom = 1f;
        private const float ZoomMin = 0.25f;
        private const float ZoomMax = 4f;
        private const float ZoomStep = 0.1f;
        private bool _isPanning;
        private Vector2 _panStart;

        private VisualElement _toolsContainer,
            _paletteContainer,
            _ropeListContainer,
            _validationContainer,
            _validationStatusDot;

        private ColorField _gridColorField;
        private VisualElement _bgLayer;

        private readonly Dictionary<Tool, Button> _toolButtons = new();
        private readonly Dictionary<string, System.Action> _commands = new();

        private static readonly Dictionary<Tool, string> ToolCommandIds = new()
        {
            { Tool.Pin, LevelEditorCommands.ToolPin },
            { Tool.Rope, LevelEditorCommands.ToolRope },
            { Tool.Lock, LevelEditorCommands.ToolLock },
            { Tool.Erase, LevelEditorCommands.ToolErase },
        };

        private LevelJson Level => _session != null ? _session.level : null;
        private StageJson Stage => Level is { stages: { Count: > 0 } } ? Level.stages[0] : null;

        [MenuItem("TwistedTangle/Level Creation Tool", false, 0)]
        public static void ShowWindow()
        {
            var w = GetWindow<LevelCreator>();
            w.titleContent = new GUIContent("Tangle Level Creator");
            w.minSize = new Vector2(700, 500);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList(Css.Root);

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            _palette = FindPalette();

            var app = new VisualElement();
            app.AddToClassList(Css.AppContainer);
            app.Add(BuildEditorSetupBar());
            app.Add(BuildTopBar());
            app.Add(BuildLevelPropsBar());

            var body = new VisualElement();
            body.AddToClassList(Css.Body);
            var rightPanel = BuildRightPanel();
            body.Add(BuildCanvasPanelWrapper());
            body.Add(BuildRightPanelDivider(rightPanel));
            body.Add(rightPanel);
            app.Add(body);
            root.Add(app);

            BuildCommandTable();
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnShortcutKeyDown);
            KeyBindingStore.Changed -= UpdateShortcutHints;
            KeyBindingStore.Changed += UpdateShortcutHints;
            EnvironmentSettings.Changed -= ApplyBackgroundToCanvas;
            EnvironmentSettings.Changed += ApplyBackgroundToCanvas;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;

            RefreshAll();
            UpdateShortcutHints();
            ApplyBackgroundToCanvas();
        }

        private void OnDisable()
        {
            KeyBindingStore.Changed -= UpdateShortcutHints;
            EnvironmentSettings.Changed -= ApplyBackgroundToCanvas;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            SyncFieldsFromLevel();
            RefreshAll();
        }

        private static RopePaletteSO FindPalette()
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(RopePaletteSO)}"))
            {
                var palette = AssetDatabase.LoadAssetAtPath<RopePaletteSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (palette != null) return palette;
            }
            return null;
        }

        #region UI: static sections

        private VisualElement BuildHelpSection()
        {
            var foldout = new Foldout { text = "ⓘ  How to use — click to expand", value = false };
            foldout.AddToClassList(Css.Section);
            foldout.Add(new HelpBox(
                "Levels are the JSON files the game loads (Resources/Levels/level_NNN.json).\n\n" +
                "EDIT AN EXISTING LEVEL\n" +
                "1. Enter the level number, click 'Load'.\n\n" +
                "NEW LEVEL\n" +
                "2. Set Level # and Map (map N = (3+N)×(5+N) grid), click 'New Level'.\n" +
                "3. Pin tool: click a cell to add a pin, drag a pin to move it, right-click to remove.\n" +
                "4. Rope tool: pick a color, click two pins. Rope reach is limited by max tension.\n" +
                "5. Lock tool: click a pin to lock/unlock it.\n" +
                "6. Tutorial (optional): 'Pick' → click the pin, then the target cell.\n\n" +
                "AI (any chat): AI Generate ↗ → copy prompt → paste the JSON answer → Import.\n\n" +
                "CHECK & SAVE\n" +
                "7. 'Validate' must be green (same rules as runtime).\n" +
                "8. Click 'Save' — writes level_NNN.json.",
                HelpBoxMessageType.Info));
            return foldout;
        }

        private VisualElement BuildEditorSetupBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList(Css.SetupBar);
            bar.Add(MakeButton("AI Generate ↗", AiLevelGeneratorWindow.ShowWindow, Css.BtnPrimary));
            bar.Add(MakeButton("Advanced Tools ↗", AdvancedToolsWindow.ShowWindow, null));
            bar.Add(MakeButton("Key Bindings ↗", KeyBindingWindow.ShowWindow, null));
            bar.Add(MakeButton("Paths ↗", PathSettingsWindow.ShowWindow, null));
            bar.Add(MakeButton("Environment ↗", EnvironmentSettingsWindow.ShowWindow, null));
            return bar;
        }

        private VisualElement BuildTopBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList(Css.Topbar);

            _levelNumberField = CompactIntField("Level #", 1);
            bar.Add(_levelNumberField);
            bar.Add(MakeButton("Load", () => LoadLevel(_levelNumberField.value), Css.BtnPrimary));
            bar.Add(MakeButton("Save", SaveCurrentLevel, Css.BtnSave));
            bar.Add(MakeButton("Delete", () => DeleteLevel(_levelNumberField.value), Css.BtnDanger));

            var sep = new VisualElement();
            sep.AddToClassList(Css.TopbarSep);
            bar.Add(sep);

            _mapField = CompactIntField("Map", 1);
            _mapField.tooltip = "Map id: grid = (3+N) columns × (5+N) rows.";
            _mapField.RegisterValueChangedCallback(e => OnMapChanged(e.newValue));
            bar.Add(_mapField);
            _gridSizeLabel = new Label();
            _gridSizeLabel.AddToClassList(Css.LevelPropsBarLabel);
            bar.Add(_gridSizeLabel);
            bar.Add(MakeButton("New Level", NewLevel, Css.BtnPrimary));
            UpdateGridSizeLabel();

            return bar;
        }

        private VisualElement BuildLevelPropsBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList(Css.LevelPropsBar);

            var diffLbl = new Label("Difficulty");
            diffLbl.AddToClassList(Css.LevelPropsBarLabel);
            bar.Add(diffLbl);
            _difficultyField = new DropdownField(new List<string>(DifficultyChoices), 0);
            _difficultyField.style.minWidth = 80;
            _difficultyField.style.marginRight = 12;
            _difficultyField.RegisterValueChangedCallback(e =>
            {
                if (Level == null || Level.difficulty == e.newValue) return;
                Undo.RecordObject(_session, "Difficulty");
                Level.difficulty = e.newValue;
            });
            bar.Add(_difficultyField);

            _moveLimitField = CompactIntField("Moves", 0);
            _moveLimitField.tooltip = "Move limit (0 = unlimited).";
            _moveLimitField.RegisterValueChangedCallback(e =>
            {
                if (Level == null || Level.moveLimit == e.newValue) return;
                Undo.RecordObject(_session, "Move Limit");
                Level.moveLimit = Mathf.Max(0, e.newValue);
            });
            bar.Add(_moveLimitField);

            _timeLimitField = CompactIntField("Time(s)", 0);
            _timeLimitField.tooltip = "Time limit in seconds (0 = unlimited).";
            _timeLimitField.RegisterValueChangedCallback(e =>
            {
                if (Level == null || Level.timeLimitSeconds == e.newValue) return;
                Undo.RecordObject(_session, "Time Limit");
                Level.timeLimitSeconds = Mathf.Max(0, e.newValue);
            });
            bar.Add(_timeLimitField);

            var gridLbl = new Label("Grid");
            gridLbl.AddToClassList(Css.LevelPropsBarLabel);
            gridLbl.style.marginLeft = 12;
            bar.Add(gridLbl);
            _gridColorField = new ColorField { showAlpha = true, value = EditorColors.GridDefault };
            _gridColorField.style.width = 80;
            _gridColorField.RegisterValueChangedCallback(evt =>
            {
                if (_canvas == null) return;
                _canvas.GridStrokeColor = evt.newValue;
                _canvas.MarkDirtyRepaint();
            });
            bar.Add(_gridColorField);

            return bar;
        }

        private VisualElement BuildCanvasPanelWrapper()
        {
            var panel = new VisualElement();
            panel.AddToClassList(Css.CanvasPanel);

            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 0;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0f;

            _canvasHost = new VisualElement();
            _canvasHost.AddToClassList(Css.CanvasHost);

            _bgLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _bgLayer.style.position = Position.Absolute;
            _bgLayer.style.top = 0;
            _bgLayer.style.left = 0;
            _bgLayer.style.width = Length.Percent(100);
            _bgLayer.style.height = Length.Percent(100);
            _canvasHost.Add(_bgLayer);

            _canvas = new RopeCanvasElement();
            _canvas.AddToClassList(Css.Canvas);
            _canvas.CellClicked = OnCanvasCellClicked;
            _canvas.CellDragged = OnCanvasCellDragged;
            _canvas.Released = OnCanvasReleased;

            _canvasHost.Add(_canvas);
            scroll.Add(_canvasHost);
            panel.Add(scroll);

            scroll.RegisterCallback<WheelEvent>(e =>
            {
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom - e.delta.y * ZoomStep, ZoomMin, ZoomMax);
                if (Mathf.Approximately(oldZoom, _zoom))
                {
                    e.StopPropagation();
                    return;
                }

                if (e.ctrlKey)
                {
                    _canvas.transform.position = Vector3.zero;
                }
                else
                {
                    Vector2 q = _canvas.WorldToLocal(e.mousePosition);
                    Vector3 oldPos = _canvas.transform.position;
                    _canvas.transform.position = new Vector3(
                        oldPos.x + q.x * (oldZoom - _zoom),
                        oldPos.y + q.y * (oldZoom - _zoom),
                        0f);
                }

                _canvas.transform.scale = new Vector3(_zoom, _zoom, 1f);
                UpdateZoomLabel();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 2) return;
                _isPanning = true;
                _panStart = e.position;
                scroll.CapturePointer(e.pointerId);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_isPanning) return;
                Vector2 delta = (Vector2)e.position - _panStart;
                _panStart = e.position;
                Vector3 oldPos = _canvas.transform.position;
                _canvas.transform.position = new Vector3(oldPos.x + delta.x, oldPos.y + delta.y, 0f);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 2 || !_isPanning) return;
                _isPanning = false;
                scroll.ReleasePointer(e.pointerId);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            var zoomRow = new VisualElement();
            zoomRow.style.position = Position.Absolute;
            zoomRow.style.top = 6f;
            zoomRow.style.right = 6f;
            zoomRow.style.flexDirection = FlexDirection.Row;
            zoomRow.style.alignItems = Align.Center;

            _zoomLabel = new Label("100%");
            _zoomLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            _zoomLabel.style.fontSize = 11f;
            _zoomLabel.style.marginRight = 4f;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _zoomLabel.style.minWidth = 38f;

            var resetZoomBtn = new Button(ResetZoom) { text = "1:1" };
            resetZoomBtn.AddToClassList(Css.Tool);
            resetZoomBtn.tooltip = "Reset zoom";
            resetZoomBtn.style.width = 36f;
            resetZoomBtn.style.height = 24f;
            resetZoomBtn.style.fontSize = 11f;

            zoomRow.Add(_zoomLabel);
            zoomRow.Add(resetZoomBtn);
            panel.Add(zoomRow);

            panel.Add(BuildRopeBar());

            var spacer = new VisualElement();
            spacer.style.flexShrink = 0;
            spacer.style.height = 160f;
            panel.Add(spacer);

            panel.Add(BuildFloatingBottomPanel());
            return panel;
        }

        private VisualElement BuildRopeBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList(Css.RopeBar);
            bar.style.flexShrink = 0;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.RopeBarScroll);
            scroll.style.flexGrow = 1;

            _ropeListContainer = new VisualElement();
            scroll.Add(_ropeListContainer);
            bar.Add(scroll);
            return bar;
        }

        private VisualElement BuildRightPanelDivider(VisualElement rightPanel)
        {
            var handle = new VisualElement();
            handle.AddToClassList(Css.RightDivider);
            float startX = 0f, startW = 0f;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                startX = e.position.x;
                startW = rightPanel.resolvedStyle.width;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                float delta = startX - e.position.x; // drag left = wider
                rightPanel.style.width = Mathf.Clamp(startW + delta, 240f, 700f);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                handle.ReleasePointer(e.pointerId);
                e.StopPropagation();
            });
            return handle;
        }

        private VisualElement BuildRightPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList(Css.RightPanel);
            panel.style.width = 320f;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.RightScroll);

            var tools = MakeSection("Tools");
            _toolsContainer = MakeRow();
            _toolsContainer.AddToClassList(Css.RowWrap);
            AddToolButton(Tool.Pin, "Pin");
            AddToolButton(Tool.Rope, "Rope");
            AddToolButton(Tool.Lock, "Lock");
            AddToolButton(Tool.Erase, "Erase");
            tools.Add(_toolsContainer);
            scroll.Add(tools);

            var brush = MakeSection("Brush");
            _paletteContainer = new VisualElement();
            brush.Add(_paletteContainer);
            scroll.Add(brush);

            scroll.Add(BuildTutorialSection());
            scroll.Add(BuildHelpSection());

            panel.Add(scroll);
            return panel;
        }

        private VisualElement BuildTutorialSection()
        {
            var s = MakeSection("Tutorial", expanded: false);
            _tutorialLabel = new Label();
            _tutorialLabel.AddToClassList(Css.Hint);
            s.Add(_tutorialLabel);
            var row = MakeRow();
            row.Add(MakeButton("Pick", () => SetTool(Tool.Tutorial), Css.BtnPrimary));
            row.Add(MakeButton("Clear", ClearTutorial, Css.BtnDanger));
            s.Add(row);
            return s;
        }

        private void AddToolButton(Tool tool, string label)
        {
            var btn = new Button(() => SetTool(tool)) { text = label };
            btn.AddToClassList(Css.Tool);
            _toolButtons[tool] = btn;
            _toolsContainer.Add(btn);
        }

        private void ApplyBackgroundToCanvas()
        {
            if (_canvasHost == null || _bgLayer == null) return;

            var mat = EnvironmentSettings.DefaultBackgroundMaterial;
            if (mat == null)
            {
                _bgLayer.style.backgroundImage = StyleKeyword.None;
                _bgLayer.style.backgroundColor = Color.clear;
                _canvasHost.style.backgroundColor = EditorColors.CanvasBg;
                if (_canvas != null) _canvas.RopeOutlineColor = EditorColors.RopeOutlineDark;
                return;
            }

            var tex = ExtractTexture(mat);
            Color bgColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : mat.HasProperty("_Color") ? mat.GetColor("_Color")
                : Color.gray;
            if (tex != null)
            {
                _bgLayer.style.backgroundImage = new StyleBackground(tex);
                _bgLayer.style.backgroundColor = Color.clear;
                _bgLayer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            else
            {
                _bgLayer.style.backgroundImage = StyleKeyword.None;
                _bgLayer.style.backgroundColor = bgColor;
            }

            _canvasHost.style.backgroundColor = Color.clear;
            if (_canvas == null) return;
            float lum = bgColor.r * 0.2126f + bgColor.g * 0.7152f + bgColor.b * 0.0722f;
            var grid = lum > 0.4f ? new Color(0f, 0f, 0f, 0.22f) : new Color(1f, 1f, 1f, 0.22f);
            _canvas.GridStrokeColor = grid;
            _canvas.RopeOutlineColor = EditorColors.RopeOutlineLight;
            _gridColorField?.SetValueWithoutNotify(grid);
            _canvas.MarkDirtyRepaint();
        }

        private static Texture2D ExtractTexture(Material m)
        {
            foreach (var name in m.GetTexturePropertyNames())
                if (m.GetTexture(name) is Texture2D tex)
                    return tex;
            return null;
        }

        private VisualElement BuildFloatingBottomPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList(Css.CanvasBottomHalf);
            panel.style.position = Position.Absolute;
            panel.style.bottom = 0;
            panel.style.left = 0;
            panel.style.width = Length.Percent(100);
            panel.style.height = 160f;

            var handle = new VisualElement();
            handle.AddToClassList(Css.CanvasBottomHandle);
            float startY = 0f, startH = 0f;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                startY = e.position.y;
                startH = panel.resolvedStyle.height;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                panel.style.height = Mathf.Clamp(startH + (startY - e.position.y), 60f, 500f);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                handle.ReleasePointer(e.pointerId);
                e.StopPropagation();
            });
            panel.Add(handle);

            var header = MakeRow();
            _validationStatusDot = new VisualElement();
            _validationStatusDot.AddToClassList(Css.StatusDot);
            header.Add(_validationStatusDot);
            header.Add(MakeButton("Validate", RebuildValidation, Css.BtnPrimary));
            panel.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.CanvasBottomScroll);
            scroll.style.flexGrow = 1;
            _validationContainer = new VisualElement();
            scroll.Add(_validationContainer);
            panel.Add(scroll);

            return panel;
        }

        #endregion

        #region UI: dynamic panels

        private void RebuildPalette()
        {
            if (_paletteContainer == null) return;
            _paletteContainer.Clear();
            switch (_tool)
            {
                case Tool.Pin:
                    _paletteContainer.Add(new Label("Pin: click a cell to add, drag a pin to move it, right-click to remove."));
                    break;
                case Tool.Rope:
                    BuildRopePalette();
                    break;
                case Tool.Lock:
                    _paletteContainer.Add(new Label("Lock: click a pin to lock / unlock it (locked pins can't be moved)."));
                    break;
                case Tool.Erase:
                    _paletteContainer.Add(new Label("Erase: click a pin to remove it together with its ropes."));
                    break;
                case Tool.Tutorial:
                    _paletteContainer.Add(new Label(_pendingPinId < 0
                        ? "Tutorial: click the pin the hand should drag."
                        : $"Tutorial: click the empty target cell for pin {_pendingPinId}."));
                    break;
            }
        }

        private void BuildRopePalette()
        {
            if (_palette == null)
            {
                _paletteContainer.Add(new HelpBox(
                    "No RopePaletteSO found. Create one: Assets ▸ Create ▸ TwistedTangle ▸ Rope Palette.",
                    HelpBoxMessageType.Warning));
                return;
            }

            _paletteContainer.Add(new Label("Click two pins to connect them. Color:"));
            var swRow = new VisualElement();
            swRow.AddToClassList(Css.SwatchGrid);
            for (int i = 0; i < _palette.Count; i++)
            {
                int index = i;
                var b = new Button(() =>
                {
                    _colorIndex = index;
                    RebuildPalette();
                }) { tooltip = $"colorIndex {index}" };
                b.AddToClassList(Css.Swatch);
                b.style.backgroundColor = _palette.Get(index);
                if (index == _colorIndex) b.AddToClassList(Css.SwatchSelected);
                swRow.Add(b);
            }
            _paletteContainer.Add(swRow);

            var actionRow = MakeRow();
            actionRow.style.marginTop = 6;
            actionRow.Add(MakeButton("Cancel Rope", CancelRope, Css.BtnDanger));
            _paletteContainer.Add(actionRow);
        }

        private void RebuildRopeList()
        {
            _ropeListContainer.Clear();
            if (Stage == null || Stage.ropes.Count == 0)
            {
                _ropeListContainer.Add(new Label("No ropes yet. Pick the Rope tool and click two pins."));
                return;
            }

            foreach (var rope in Stage.ropes.OrderByDescending(r => r.layer))
            {
                var captured = rope;

                var row = new VisualElement();
                row.AddToClassList(Css.RopeRow);
                if (rope.id == _selectedRopeId) row.AddToClassList(Css.RopeRowSelected);

                var left = new VisualElement();
                left.AddToClassList(Css.RopeRowLeft);
                left.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedRopeId = captured.id;
                    RefreshAll();
                });

                var handle = new Label("≡");
                handle.AddToClassList(Css.RopeRowHandle);
                left.Add(handle);

                var swatch = new VisualElement();
                swatch.AddToClassList(Css.RopeRowSwatch);
                swatch.style.backgroundColor = _palette != null ? _palette.Get(rope.colorIndex) : Color.white;
                if (_tool == Tool.Rope) swatch.AddToClassList(Css.RopeRowSwatchPaintable);
                swatch.tooltip = _tool == Tool.Rope ? "Click to apply the selected color" : string.Empty;
                swatch.RegisterCallback<ClickEvent>(e =>
                {
                    if (_tool != Tool.Rope) return;
                    e.StopPropagation(); // don't also trigger the row selection click
                    Undo.RecordObject(_session, "Recolor Rope");
                    captured.colorIndex = _colorIndex;
                    RefreshAll();
                });
                left.Add(swatch);

                var info = new VisualElement();
                info.AddToClassList(Css.RopeRowInfo);
                var nameLabel = new Label($"Rope {rope.id}");
                nameLabel.AddToClassList(Css.RopeRowName);
                string shape = rope.path is { Count: >= 2 } ? $"path {rope.path.Count} pts" : "straight";
                var metaLabel = new Label($"pin {rope.pinA} → {rope.pinB} · {shape}");
                metaLabel.AddToClassList(Css.RopeRowMeta);
                info.Add(nameLabel);
                info.Add(metaLabel);
                left.Add(info);

                var badge = new Label($"L{rope.layer}");
                badge.AddToClassList(Css.RopeRowBadge);
                left.Add(badge);

                row.Add(left);

                var actions = new VisualElement();
                actions.AddToClassList(Css.RopeRowActions);
                actions.Add(RopeIconButton("↑", "Bring to front", () => BringToFront(captured)));
                actions.Add(RopeIconButton("↓", "Send to back", () => SendToBack(captured)));
                var deleteBtn = RopeIconButton("✕", "Delete rope", () => DeleteRope(captured));
                deleteBtn.AddToClassList(Css.RopeRowIconBtnDanger);
                actions.Add(deleteBtn);

                row.Add(actions);
                _ropeListContainer.Add(row);
            }
        }

        private Button RopeIconButton(string text, string tooltip, System.Action action)
        {
            var b = new Button(() =>
            {
                action();
                RefreshAll();
            }) { text = text, tooltip = tooltip };
            b.AddToClassList(Css.RopeRowIconBtn);
            return b;
        }

        private ValidationReport RebuildValidationInternal()
        {
            _validationContainer.Clear();
            if (Level == null)
            {
                _validationContainer.Add(new Label("Load a level or create a new one."));
                SetStatusDot(null);
                return null;
            }

            var report = LevelValidator.Validate(Level);

            var status = new Label(report.IsValid ? "✓ Level is valid" : $"✗ {report.Errors.Count} error(s)");
            status.AddToClassList(report.IsValid ? Css.ValidationOk : Css.ValidationError);
            _validationContainer.Add(status);

            foreach (var err in report.Errors)
            {
                var l = new Label("• " + err);
                l.AddToClassList(Css.ValidationError);
                _validationContainer.Add(l);
            }

            foreach (var warn in report.Warnings)
            {
                var l = new Label("• " + warn);
                l.AddToClassList(Css.ValidationWarn);
                _validationContainer.Add(l);
            }

            var m = report.Metrics;
            var metricsRow = MakeRow();
            metricsRow.AddToClassList(Css.RowWrap);
            AddMetricChip(metricsRow, m.PinCount.ToString(), "pins");
            AddMetricChip(metricsRow, m.LockedPinCount.ToString(), "locked");
            AddMetricChip(metricsRow, m.RopeCount.ToString(), "ropes");
            AddMetricChip(metricsRow, m.CrossingPairs.ToString(), "crossings", m.CrossingPairs == 0);
            AddMetricChip(metricsRow, m.TangledRopes.ToString(), "tangled");
            AddMetricChip(metricsRow, m.FreeCells.ToString(), "free cells");
            AddMetricChip(metricsRow, Level.moveLimit > 0 ? Level.moveLimit.ToString() : "∞", "moves");
            AddMetricChip(metricsRow, Level.timeLimitSeconds > 0 ? $"{Level.timeLimitSeconds}s" : "∞", "time");
            _validationContainer.Add(metricsRow);

            SetStatusDot(report.IsValid);
            return report;
        }

        private void RebuildValidation() => RebuildValidationInternal();

        private void SetStatusDot(bool? ok)
        {
            if (_validationStatusDot == null) return;
            _validationStatusDot.EnableInClassList(Css.StatusDotOk, ok == true);
            _validationStatusDot.EnableInClassList(Css.StatusDotError, ok == false);
            _validationStatusDot.EnableInClassList(Css.StatusDotWarn, false);
        }

        private static void AddMetricChip(VisualElement row, string value, string label, bool warn = false)
        {
            var chip = new VisualElement();
            chip.AddToClassList(Css.MetricChip);
            if (warn) chip.AddToClassList(Css.MetricChipWarn);

            var valLbl = new Label(value);
            valLbl.AddToClassList(Css.MetricChipVal);
            var nameLbl = new Label(label);
            nameLbl.AddToClassList(Css.MetricChipLbl);

            chip.Add(valLbl);
            chip.Add(nameLbl);
            row.Add(chip);
        }

        private void UpdateTutorialLabel()
        {
            if (_tutorialLabel == null) return;
            var t = Level?.tutorial;
            _tutorialLabel.text = t != null && t.pinId >= 0
                ? $"Hand drags pin {t.pinId} → cell ({t.toX},{t.toY}); other pins locked: {t.lockOtherPins}"
                : "No tutorial on this level.";
        }

        #endregion

        #region Canvas interaction

        private PinJson PinAt(Vector2Int cell) => Stage?.pins.FirstOrDefault(p => p.Cell == cell);
        private PinJson PinById(int id) => Stage?.pins.FirstOrDefault(p => p.id == id);

        private void OnCanvasCellClicked(int x, int y, int button)
        {
            if (Stage == null) return;
            var cell = new Vector2Int(x, y);
            var pin = PinAt(cell);

            switch (_tool)
            {
                case Tool.Pin:
                    if (button == 1)
                    {
                        if (pin != null) RemovePin(pin);
                    }
                    else if (pin != null) BeginPinDrag(pin);
                    else AddPin(cell);
                    break;
                case Tool.Rope:
                    if (button == 1) CancelRope();
                    else OnRopeClick(pin);
                    break;
                case Tool.Lock:
                    if (pin != null) ToggleLock(pin);
                    break;
                case Tool.Erase:
                    if (pin != null) RemovePin(pin);
                    break;
                case Tool.Tutorial:
                    OnTutorialClick(cell, pin);
                    break;
            }

            RefreshAll();
        }

        private void OnCanvasCellDragged(int x, int y)
        {
            if (_draggingPinId < 0 || Stage == null) return;
            var cell = new Vector2Int(x, y);
            var pin = PinById(_draggingPinId);
            if (pin == null || PinAt(cell) != null) return;
            MovePin(pin, cell);
            RefreshCanvas();
        }

        private void OnCanvasReleased()
        {
            if (_draggingPinId < 0) return;
            _draggingPinId = -1;
            _dragBasePaths.Clear();
            RefreshAll();
        }

        #endregion

        #region Model mutations

        private void NewLevel()
        {
            int map = Mathf.Max(1, _mapField.value);
            var level = new LevelJson
            {
                levelNumber = Mathf.Max(1, _levelNumberField.value),
                difficulty = _difficultyField.value,
                moveLimit = Mathf.Max(0, _moveLimitField.value),
                timeLimitSeconds = Mathf.Max(0, _timeLimitField.value),
                tutorial = new TutorialJson(),
            };
            level.stages.Add(new StageJson
            {
                mapId = map,
                gridWidth = StageJson.WidthForMap(map),
                gridHeight = StageJson.HeightForMap(map),
            });
            SetLevel(level, 0);
        }

        private void OnMapChanged(int map)
        {
            UpdateGridSizeLabel();
            if (Stage == null || map < 1 || Stage.mapId == map) return;
            Undo.RecordObject(_session, "Change Map");
            Stage.mapId = map;
            Stage.gridWidth = StageJson.WidthForMap(map);
            Stage.gridHeight = StageJson.HeightForMap(map);
            RefreshAll();
        }

        private void UpdateGridSizeLabel()
        {
            if (_gridSizeLabel == null || _mapField == null) return;
            int map = Mathf.Max(1, _mapField.value);
            _gridSizeLabel.text = $"{StageJson.WidthForMap(map)}×{StageJson.HeightForMap(map)}";
        }

        private void AddPin(Vector2Int cell)
        {
            Undo.RecordObject(_session, "Add Pin");
            int id = Stage.pins.Count == 0 ? 0 : Stage.pins.Max(p => p.id) + 1;
            Stage.pins.Add(new PinJson { id = id, x = cell.x, y = cell.y });
        }

        private void RemovePin(PinJson pin)
        {
            Undo.RecordObject(_session, "Remove Pin");
            Stage.pins.Remove(pin);
            Stage.ropes.RemoveAll(r => r.pinA == pin.id || r.pinB == pin.id);
            if (Level.tutorial != null && Level.tutorial.pinId == pin.id) Level.tutorial.pinId = -1;
            if (_pendingPinId == pin.id) _pendingPinId = -1;
        }

        private void ToggleLock(PinJson pin)
        {
            Undo.RecordObject(_session, "Toggle Pin Lock");
            pin.locked = !pin.locked;
        }

        private void BeginPinDrag(PinJson pin)
        {
            Undo.RecordObject(_session, "Move Pin");
            _draggingPinId = pin.id;
            _dragBasePaths.Clear();
            foreach (var rope in Stage.ropes)
            {
                if ((rope.pinA != pin.id && rope.pinB != pin.id) || rope.path == null || rope.path.Count < 2) continue;
                var pts = rope.path.Select(p => new Vector2(p.x, p.y)).ToArray();
                _dragBasePaths[rope.id] = (pts, RopeGeometry.PathLength(pts));
            }
        }

        // Moves a pin; attached authored paths are re-fitted with the runtime's RopeShape remap.
        private void MovePin(PinJson pin, Vector2Int cell)
        {
            pin.x = cell.x;
            pin.y = cell.y;
            foreach (var rope in Stage.ropes)
            {
                if (!_dragBasePaths.TryGetValue(rope.id, out var basePath)) continue;
                var a = PinById(rope.pinA);
                var b = PinById(rope.pinB);
                if (a == null || b == null) continue;
                var result = new Vector2[basePath.path.Length];
                RopeShape.Remap(basePath.path, basePath.length, a.Cell, b.Cell, result);
                for (int i = 0; i < result.Length; i++)
                    rope.path[i] = new PathPointJson { x = result[i].x, y = result[i].y, h = rope.path[i].h };
            }
        }

        private void OnRopeClick(PinJson pin)
        {
            if (pin == null || pin.id == _pendingPinId)
            {
                _pendingPinId = -1;
                return;
            }

            if (_pendingPinId < 0)
            {
                _pendingPinId = pin.id;
                return;
            }

            var first = PinById(_pendingPinId);
            if (first == null)
            {
                _pendingPinId = pin.id;
                return;
            }

            if (Stage.ropes.Any(r => (r.pinA == first.id && r.pinB == pin.id) || (r.pinA == pin.id && r.pinB == first.id)))
            {
                ShowNotification(new GUIContent("These pins are already connected."));
                return;
            }

            float max = RopeTensionRule.MaxLength(RopeTensionRule.RestLength);
            if (Vector2.Distance(first.Cell, pin.Cell) > max)
            {
                ShowNotification(new GUIContent($"Too far — max rope length is {max:0.00} cells."));
                return;
            }

            Undo.RecordObject(_session, "Add Rope");
            var rope = new RopeJson
            {
                id = Stage.ropes.Count == 0 ? 0 : Stage.ropes.Max(r => r.id) + 1,
                pinA = first.id,
                pinB = pin.id,
                colorIndex = _colorIndex,
                layer = Stage.ropes.Count == 0 ? 0 : Stage.ropes.Max(r => r.layer) + 1,
            };
            Stage.ropes.Add(rope);
            _selectedRopeId = rope.id;
            _pendingPinId = -1;
        }

        private void CancelRope()
        {
            _pendingPinId = -1;
            RefreshAll();
        }

        private void DeleteRope(RopeJson rope)
        {
            Undo.RecordObject(_session, "Delete Rope");
            Stage.ropes.Remove(rope);
            if (_selectedRopeId == rope.id) _selectedRopeId = -1;
        }

        private void BringToFront(RopeJson rope)
        {
            if (Stage.ropes.Count <= 1) return;
            Undo.RecordObject(_session, "Rope To Front");
            rope.layer = Stage.ropes.Max(r => r.layer) + 1;
        }

        private void SendToBack(RopeJson rope)
        {
            if (Stage.ropes.Count <= 1) return;
            Undo.RecordObject(_session, "Rope To Back");
            rope.layer = Stage.ropes.Min(r => r.layer) - 1;
        }

        private void OnTutorialClick(Vector2Int cell, PinJson pin)
        {
            if (_pendingPinId < 0)
            {
                if (pin != null) _pendingPinId = pin.id;
                return;
            }

            if (pin != null)
            {
                _pendingPinId = pin.id;
                return;
            }

            Undo.RecordObject(_session, "Set Tutorial");
            Level.tutorial ??= new TutorialJson();
            Level.tutorial.pinId = _pendingPinId;
            Level.tutorial.toX = cell.x;
            Level.tutorial.toY = cell.y;
            Level.tutorial.lockOtherPins = true;
            _pendingPinId = -1;
            _tool = Tool.Pin;
        }

        private void ClearTutorial()
        {
            if (Level?.tutorial == null) return;
            Undo.RecordObject(_session, "Clear Tutorial");
            Level.tutorial.pinId = -1;
            RefreshAll();
        }

        #endregion

        #region Save / load / delete

        private void SetLevel(LevelJson level, int loadedFrom)
        {
            _session = LevelEditSession.Create(level);
            _loadedLevelNumber = loadedFrom;
            _pendingPinId = -1;
            _selectedRopeId = -1;
            _draggingPinId = -1;
            SyncFieldsFromLevel();
            RefreshAll();
        }

        private void SyncFieldsFromLevel()
        {
            if (Level == null) return;
            _levelNumberField.SetValueWithoutNotify(Level.levelNumber);
            if (Stage != null) _mapField.SetValueWithoutNotify(Stage.mapId);
            UpdateGridSizeLabel();
            if (!_difficultyField.choices.Contains(Level.difficulty))
                _difficultyField.choices.Add(Level.difficulty);
            _difficultyField.SetValueWithoutNotify(Level.difficulty);
            _moveLimitField.SetValueWithoutNotify(Level.moveLimit);
            _timeLimitField.SetValueWithoutNotify(Level.timeLimitSeconds);
        }

        public void LoadGeneratedLevel(LevelJson level)
        {
            level.levelNumber = Mathf.Max(1, _levelNumberField.value);
            SetLevel(level, 0);
        }

        private void SaveCurrentLevel()
        {
            if (Level == null)
            {
                EditorUtility.DisplayDialog("Save", "Load a level or create a new one first.", "OK");
                return;
            }

            Level.levelNumber = _levelNumberField.value;
            var report = RebuildValidationInternal();
            if (report is { IsValid: false })
            {
                EditorUtility.DisplayDialog("Cannot save — level has errors",
                    string.Join("\n", report.Errors.Take(10)), "OK");
                return;
            }

            int n = Level.levelNumber;
            if (n != _loadedLevelNumber && LevelFileUtility.Exists(n) &&
                !EditorUtility.DisplayDialog("Overwrite", $"level_{n:000}.json already exists. Overwrite it?",
                    "Overwrite", "Cancel"))
                return;

            if (LevelFileUtility.Save(Level))
            {
                _loadedLevelNumber = n;
                ShowNotification(new GUIContent($"Saved {LevelFileUtility.PathFor(n)}"));
            }
        }

        private void LoadLevel(int levelNumber)
        {
            var level = LevelFileUtility.Load(levelNumber);
            if (level == null || level.stages == null || level.stages.Count == 0)
            {
                EditorUtility.DisplayDialog("Load", $"No level file {LevelFileUtility.PathFor(levelNumber)}.", "OK");
                return;
            }

            SetLevel(level, levelNumber);
        }

        private void DeleteLevel(int levelNumber)
        {
            if (!LevelFileUtility.Exists(levelNumber))
            {
                EditorUtility.DisplayDialog("Delete", $"No level file {LevelFileUtility.PathFor(levelNumber)}.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete", $"Delete level_{levelNumber:000}.json?", "Delete", "Cancel"))
                return;

            LevelFileUtility.Delete(levelNumber);
            if (_loadedLevelNumber == levelNumber)
            {
                _session = null;
                _loadedLevelNumber = 0;
                RefreshAll();
            }
        }

        #endregion

        #region Refresh helpers

        private void SetTool(Tool tool)
        {
            _tool = tool;
            _pendingPinId = -1;
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_canvas == null) return;
            UpdateToolActiveStates();
            RebuildPalette();
            RebuildRopeList();
            UpdateTutorialLabel();
            var report = RebuildValidationInternal();
            RefreshCanvas(report);
        }

        private void UpdateToolActiveStates()
        {
            foreach (var kv in _toolButtons)
                kv.Value.EnableInClassList(Css.ToolActive, _tool == kv.Key);
        }

        private void ResetZoom()
        {
            _zoom = 1f;
            if (_canvas == null) return;
            _canvas.transform.scale = Vector3.one;
            _canvas.transform.position = Vector3.zero;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (_zoomLabel != null)
                _zoomLabel.text = $"{Mathf.RoundToInt(_zoom * 100f)}%";
        }

        private void RefreshCanvas(ValidationReport report = null)
        {
            if (_canvas == null) return;
            _canvas.Stage = Stage;
            _canvas.Tutorial = Level?.tutorial;
            _canvas.Palette = _palette;
            _canvas.PendingPinId = _pendingPinId;
            _canvas.SelectedRopeId = _selectedRopeId;
            if (report != null) _canvas.TangledRopeIds = report.IsValid ? TangledRopes() : null;
            _canvas.Redraw();
        }

        private HashSet<int> TangledRopes()
        {
            var set = new HashSet<int>();
            foreach (var r in LevelValidator.BuildBoard(Level).Ropes)
                if (r.CrossingCount > 0)
                    set.Add(r.Id);
            return set;
        }

        #endregion

        #region Keyboard shortcuts

        private void BuildCommandTable()
        {
            _commands.Clear();
            _commands[LevelEditorCommands.ToolPin] = () => SetTool(Tool.Pin);
            _commands[LevelEditorCommands.ToolRope] = () => SetTool(Tool.Rope);
            _commands[LevelEditorCommands.ToolLock] = () => SetTool(Tool.Lock);
            _commands[LevelEditorCommands.ToolErase] = () => SetTool(Tool.Erase);

            _commands[LevelEditorCommands.Save] = SaveCurrentLevel;
            _commands[LevelEditorCommands.Load] = () => LoadLevel(_levelNumberField.value);
            _commands[LevelEditorCommands.Delete] = () => DeleteLevel(_levelNumberField.value);
            _commands[LevelEditorCommands.NewLevel] = NewLevel;

            _commands[LevelEditorCommands.CancelRope] = CancelRope;
            _commands[LevelEditorCommands.RopeToFront] = () => WithSelectedRope(BringToFront);
            _commands[LevelEditorCommands.RopeToBack] = () => WithSelectedRope(SendToBack);
            _commands[LevelEditorCommands.RopeDelete] = () => WithSelectedRope(DeleteRope);

            _commands[LevelEditorCommands.Validate] = RebuildValidation;
        }

        private void OnShortcutKeyDown(KeyDownEvent e)
        {
            var combo = KeyCombo.FromEvent(e);
            if (combo.IsEmpty) return;
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
                if (el is TextField || el is IntegerField || el is FloatField)
                    return true;
            return false;
        }

        private void WithSelectedRope(System.Action<RopeJson> action)
        {
            var rope = Stage?.ropes.FirstOrDefault(r => r.id == _selectedRopeId);
            if (rope == null) return;
            action(rope);
            RefreshAll();
        }

        private void UpdateShortcutHints()
        {
            foreach (var kv in _toolButtons)
                if (ToolCommandIds.TryGetValue(kv.Key, out var id))
                    kv.Value.tooltip = ShortcutTooltip(id);
        }

        private static string ShortcutTooltip(string commandId)
        {
            var combo = KeyBindingStore.Get(commandId);
            return combo.IsEmpty ? string.Empty : $"Shortcut: {combo}";
        }

        #endregion

        #region Small UI factory

        private static VisualElement MakeSection(string header, bool expanded = true)
        {
            var f = new Foldout { text = header, value = expanded };
            f.AddToClassList(Css.Section);
            return f;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList(Css.Row);
            return r;
        }

        private static Button MakeButton(string text, System.Action onClick, string ussClass)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList(Css.Btn);
            if (!string.IsNullOrEmpty(ussClass)) b.AddToClassList(ussClass);
            return b;
        }

        private static IntegerField CompactIntField(string label, int value)
        {
            var f = new IntegerField(label) { value = value };
            f.AddToClassList(Css.Num);
            return f;
        }

        #endregion
    }

    [InitializeOnLoad]
    static class LevelCreatorAutoOpen
    {
        static LevelCreatorAutoOpen()
        {
            if (SessionState.GetBool("LevelCreatorOpened", false)) return;
            EditorApplication.delayCall += () =>
            {
                SessionState.SetBool("LevelCreatorOpened", true);
                LevelCreator.ShowWindow();
            };
        }
    }
}
