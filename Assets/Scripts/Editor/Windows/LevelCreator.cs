using System.Collections.Generic;
using System.Linq;
using Editor.Windows;
using TwistedTangle.Editor.Canvas;
using TwistedTangle.Editor.Input;
using TwistedTangle.Editor.Settings;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Editor.Validation;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using TwistedTangle.Editor.Geometry;
using TwistedTangle.Runtime.Data.Enums;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    public class LevelCreator : EditorWindow
    {
        private enum Tool
        {
            Place,
            Rope,
            Erase,
            Flip
        }

        private const float FlipPickRadiusCells = 0.35f;

        private LevelDataSO _level;
        private int _currentLevelId = 0;
        private bool _isEditMode;
        private int _nextRopeId;

        private Tool _tool = Tool.Place;
        private bool _isPainting;
        private EntityBaseTypeSO _selectedBaseType;
        private EntityDefinitionSO _selectedEntity;
        private Color _ropeColor = new(0.90f, 0.20f, 0.20f);
        private Material _ropeMaterial;
        private RopeData _previewRope;
        private int _selectedRopeId = -1;
        private readonly Stack<List<RopeWaypoint>> _waypointHistory = new();

        private readonly List<EntityBaseTypeSO> _baseTypes = new();
        private readonly List<EntityDefinitionSO> _entityDefs = new();
        private readonly Dictionary<string, EntityDefinitionSO> _entityLookup = new();
        private readonly Dictionary<EntityDefinitionSO, EntityDefinitionEditorDataSO> _editorDataLookup = new();
        private readonly List<(string name, Color color)> _swatches = new();
        private readonly List<ColorPaletteSO> _paletteAssets = new();
        private int _selectedPaletteIndex = 0;
        private readonly HashSet<string> _hiddenSwatchNames = new();

        private IntegerField _levelIdField, _widthField, _heightField, _timeField;
        private EnumField _difficultyField;
        private RopeCanvasElement _canvas;
        private VisualElement _canvasHost;
        private Label _zoomLabel;
        private float _zoom = 1f;
        private const float ZoomMin = 0.25f;
        private const float ZoomMax = 4f;
        private const float ZoomStep = 0.1f;
        private bool _isPanning;
        private Vector2 _panStart;

        private const int MaxRopeReach = 3;

        private VisualElement _paletteContainer,
            _toolsContainer,
            _editToolsContainer,
            _ropeListContainer,
            _validationContainer,
            _validationStatusDot;

        private ObjectField _bgMaterialField;
        private Slider _bgOpacitySlider;
        private ColorField _gridColorField;
        private VisualElement _bgDimmerLayer;

        private readonly Dictionary<Tool, Button> _toolButtons = new();
        private readonly List<(EntityBaseTypeSO baseType, Button btn)> _baseButtons = new();
        private readonly List<(EntityDefinitionSO def, Button btn)> _entityButtons = new();

        private readonly Dictionary<string, System.Action> _commands = new();

        private static readonly Dictionary<Tool, string> ToolCommandIds = new()
        {
            { Tool.Rope, LevelEditorCommands.ToolRope },
            { Tool.Erase, LevelEditorCommands.ToolErase },
            { Tool.Flip, LevelEditorCommands.ToolFlip },
        };

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

            RefreshBaseTypes();
            RefreshEntityDefinitions();
            RefreshEditorData();
            RefreshPalettes();

            _selectedBaseType = _baseTypes.FirstOrDefault();
            _selectedEntity = SubTypesOf(_selectedBaseType).FirstOrDefault();
            _tool = (_baseTypes.Count > 0 || HasUngrouped()) ? Tool.Place : Tool.Rope;

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
            EnvironmentSettings.Changed -= OnEnvironmentChanged;
            EnvironmentSettings.Changed += OnEnvironmentChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;

            RefreshAll();
            UpdateShortcutHints();
            ApplyBackgroundToCanvas(_level?.BackgroundMaterial);
        }

        private void OnDisable()
        {
            KeyBindingStore.Changed -= UpdateShortcutHints;
            EnvironmentSettings.Changed -= OnEnvironmentChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo() => RefreshAll();

        private void OnEnvironmentChanged() => ApplyBackgroundToCanvas(_level?.BackgroundMaterial);

        #region Data-driven discovery

        private void RefreshBaseTypes()
        {
            _baseTypes.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityBaseTypeSO)}"))
            {
                var b = AssetDatabase.LoadAssetAtPath<EntityBaseTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (b != null) _baseTypes.Add(b);
            }

            _baseTypes.Sort((a, b) =>
            {
                int cmp = a.SortOrder.CompareTo(b.SortOrder);
                return cmp != 0
                    ? cmp
                    : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
            });

            if (_selectedBaseType != null && !_baseTypes.Contains(_selectedBaseType))
                _selectedBaseType = _baseTypes.FirstOrDefault();
        }

        private void RefreshEntityDefinitions()
        {
            _entityDefs.Clear();
            _entityLookup.Clear();

            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityDefinitionSO)}"))
            {
                var def = AssetDatabase.LoadAssetAtPath<EntityDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                _entityDefs.Add(def);
                _entityLookup[def.TypeId] = def;
            }

            if (_selectedEntity == null || !_entityDefs.Contains(_selectedEntity))
                _selectedEntity = SubTypesOf(_selectedBaseType).FirstOrDefault();
        }

        private void RefreshEditorData()
        {
            _editorDataLookup.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(EntityDefinitionEditorDataSO)}"))
            {
                var data = AssetDatabase.LoadAssetAtPath<EntityDefinitionEditorDataSO>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (data?.Definition != null) _editorDataLookup[data.Definition] = data;
            }
        }

        private EntityDefinitionEditorDataSO EditorDataFor(EntityDefinitionSO def) =>
            def != null && _editorDataLookup.TryGetValue(def, out var data) ? data : null;

        private IEnumerable<EntityDefinitionSO> SubTypesOf(EntityBaseTypeSO baseType) =>
            _entityDefs.Where(d => EditorDataFor(d)?.BaseType == baseType)
                .OrderBy(d => d, Comparer<EntityDefinitionSO>.Create(
                    (a, b) => LevelEditorCommands.CompareSubTypes(a, b, _editorDataLookup)));

        private bool HasUngrouped() => _entityDefs.Any(d => EditorDataFor(d)?.BaseType == null);

        private void RefreshPalettes()
        {
            _swatches.Clear();
            _paletteAssets.Clear();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(ColorPaletteSO)}"))
            {
                var pal = AssetDatabase.LoadAssetAtPath<ColorPaletteSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (pal == null) continue;
                _paletteAssets.Add(pal);
                foreach (var e in pal.Entries) _swatches.Add((e.Name, e.Color));
            }
        }

        private Color ResolveEntityColor(string typeId)
        {
            _entityLookup.TryGetValue(typeId, out var def);
            return EditorDataFor(def)?.EditorColor ?? EditorColors.EntityFallback;
        }

        private void CreateDefaultEntityTypes()
        {
            EnsureFolder(LevelEditorPaths.Bases);
            EnsureFolder(LevelEditorPaths.Entities);
            EnsureFolder(LevelEditorPaths.EntityEditorData);
            var pin = CreateBaseAsset("pin", "Pin", EditorColors.PinDefault, LevelEditorPaths.Bases);
            var standard = CreateEntityAsset("pin.standard", "Standard", null, null, LevelEditorPaths.Entities);
            CreateEntityEditorDataAsset(standard, pin, EditorColors.PinDefault, CanvasMarker.None,
                LevelEditorPaths.EntityEditorData);
            var nailed = CreateEntityAsset("pin.nailed", "Nailed", null, new[] { "nailed" }, LevelEditorPaths.Entities);
            CreateEntityEditorDataAsset(nailed, pin, new Color(1f, 0.6f, 0.1f), CanvasMarker.None,
                LevelEditorPaths.EntityEditorData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshBaseTypes();
            RefreshEntityDefinitions();
            RefreshEditorData();
            RebuildToolbar();
            _selectedBaseType = pin;
            _selectedEntity = SubTypesOf(pin).FirstOrDefault();
            _tool = Tool.Place;
            RefreshAll();
        }

        private static EntityBaseTypeSO CreateBaseAsset(string baseId, string displayName, Color color, string folder)
        {
            string path = $"{folder}/EntityBase_{Slugify(displayName)}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EntityBaseTypeSO>(path);
            if (existing != null) return existing;

            var so = CreateInstance<EntityBaseTypeSO>();
            var sObj = new SerializedObject(so);
            sObj.FindProperty("baseId").stringValue = baseId;
            sObj.FindProperty("displayName").stringValue = displayName;
            sObj.FindProperty("editorColor").colorValue = color;
            sObj.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static EntityDefinitionSO CreateEntityAsset(string typeId, string displayName,
            GameObject prefab, string[] tags, string folder)
        {
            string path = $"{folder}/Entity_{typeId.Replace('.', '_')}.asset";
            if (AssetDatabase.LoadAssetAtPath<EntityDefinitionSO>(path) != null) return null;

            var so = CreateInstance<EntityDefinitionSO>();
            var sObj = new SerializedObject(so);
            sObj.FindProperty("typeId").stringValue = typeId;
            sObj.FindProperty("displayName").stringValue = displayName;
            if (prefab != null) sObj.FindProperty("prefab").objectReferenceValue = prefab;
            if (tags is { Length: > 0 })
            {
                var tagsProp = sObj.FindProperty("tags");
                tagsProp.arraySize = tags.Length;
                for (int i = 0; i < tags.Length; i++)
                    tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i];
            }

            sObj.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static void CreateEntityEditorDataAsset(EntityDefinitionSO definition,
            EntityBaseTypeSO baseType, Color color, CanvasMarker marker, string folder)
        {
            if (definition == null) return;
            string path = $"{folder}/EntityEditorData_{definition.TypeId.Replace('.', '_')}.asset";
            if (AssetDatabase.LoadAssetAtPath<EntityDefinitionEditorDataSO>(path) != null) return;

            var so = CreateInstance<EntityDefinitionEditorDataSO>();
            var sObj = new SerializedObject(so);
            sObj.FindProperty("definition").objectReferenceValue = definition;
            if (baseType != null) sObj.FindProperty("baseType").objectReferenceValue = baseType;
            sObj.FindProperty("editorColor").colorValue = color;
            sObj.FindProperty("canvasMarker").intValue = (int)marker;
            sObj.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(so, path);
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var chars = s.Trim().ToLowerInvariant()
                .Select(c => char.IsWhiteSpace(c) ? '_' : c)
                .Where(c => char.IsLetterOrDigit(c) || c == '_');
            return new string(chars.ToArray());
        }

        private (bool ok, string error) TryCreateEntityType(EntityBaseTypeSO existingBase, string newBaseName,
            string subName, Color color, GameObject prefab)
        {
            RefreshBaseTypes();
            RefreshEntityDefinitions();

            var baseType = existingBase;
            if (baseType == null)
            {
                newBaseName = newBaseName?.Trim();
                if (string.IsNullOrEmpty(newBaseName))
                    return (false, "New base type name is required.");
                string baseId = Slugify(newBaseName);
                if (string.IsNullOrEmpty(baseId))
                    return (false, "Base type name must contain letters or digits.");
                if (_baseTypes.Any(b => string.Equals(b.BaseId, baseId, System.StringComparison.OrdinalIgnoreCase)))
                    return (false, $"A base type '{newBaseName}' already exists — pick it from the dropdown.");
                EnsureFolder(LevelEditorPaths.Bases);
                baseType = CreateBaseAsset(baseId, newBaseName, color, LevelEditorPaths.Bases);
            }

            subName = subName?.Trim();
            if (string.IsNullOrEmpty(subName))
                return (false, "Sub-type name is required.");
            string subSlug = Slugify(subName);
            if (string.IsNullOrEmpty(subSlug))
                return (false, "Sub-type name must contain letters or digits.");
            string typeId = $"{baseType.BaseId}.{subSlug}";
            if (_entityLookup.ContainsKey(typeId))
                return (false, $"“{baseType.DisplayName}” already has a sub-type '{subName}'.");

            EnsureFolder(LevelEditorPaths.Entities);
            EnsureFolder(LevelEditorPaths.EntityEditorData);
            var so = CreateEntityAsset(typeId, subName, prefab, null, LevelEditorPaths.Entities);
            if (so == null)
                return (false, $"An asset already exists for '{subName}' under '{baseType.DisplayName}'.");

            CreateEntityEditorDataAsset(so, baseType, color, CanvasMarker.None, LevelEditorPaths.EntityEditorData);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshBaseTypes();
            RefreshEntityDefinitions();
            RefreshEditorData();
            RebuildToolbar();

            _selectedBaseType = baseType;
            _selectedEntity = so;
            _tool = Tool.Place;
            RefreshAll();
            return (true, null);
        }

        private void CreateDefaultPalette()
        {
            EnsureFolder(LevelEditorPaths.Palettes);
            string path = $"{LevelEditorPaths.Palettes}/BaseGameColorPalette.asset";
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

        private (bool ok, string error) TryAddPaletteColor(ColorPaletteSO existingPalette,
            string newPaletteName, string colorName, Color color, bool autoGenerate)
        {
            colorName = colorName?.Trim();
            if (string.IsNullOrEmpty(colorName)) return (false, "Color name is required.");

            var palette = existingPalette;
            if (palette == null)
            {
                newPaletteName = newPaletteName?.Trim();
                if (string.IsNullOrEmpty(newPaletteName)) return (false, "New palette name is required.");
                EnsureFolder(LevelEditorPaths.Palettes);
                string path = $"{LevelEditorPaths.Palettes}/{newPaletteName.Replace(' ', '_')}.asset";
                palette = AssetDatabase.LoadAssetAtPath<ColorPaletteSO>(path);
                if (palette == null)
                {
                    palette = CreateInstance<ColorPaletteSO>();
                    AssetDatabase.CreateAsset(palette, path);
                }
            }

            foreach (var e in palette.Entries)
                if (string.Equals(e.Name, colorName, System.StringComparison.OrdinalIgnoreCase))
                    return (false, $"'{palette.name}' already has a color named '{colorName}'.");

            var so = new SerializedObject(palette);
            var entries = so.FindProperty("entries");
            entries.arraySize++;
            var el = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            el.FindPropertyRelative("Name").stringValue = colorName;
            el.FindPropertyRelative("Color").colorValue = color;

            Material variant = null;
            if (autoGenerate && palette.VariantTemplate != null)
            {
                var repo = new TwistedTangle.Editor.Materials.MaterialVariantRepository(
                    LevelEditorPaths.MaterialsForPalette(palette.name),
                    new TwistedTangle.Editor.Materials.MaterialVariantFactory());
                variant = repo.GetOrCreate(palette.VariantTemplate, colorName, color);
                el.FindPropertyRelative("Variant").objectReferenceValue = variant;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();

            RefreshPalettes();
            int idx = _paletteAssets.IndexOf(palette);
            if (idx >= 0) _selectedPaletteIndex = idx;
            _ropeColor = color; // auto-select the newly added color
            _ropeMaterial = variant;
            RebuildPalette();
            return (true, null);
        }

        #endregion

        #region UI: static sections

        private VisualElement BuildHelpSection()
        {
            var foldout = new Foldout { text = "ⓘ  How to use — click to expand", value = false };
            foldout.AddToClassList(Css.Section);
            foldout.Add(new HelpBox(
                "QUICK START\n" +
                "1. Set Width / Height / Time, then click 'Generate Grid'.\n" +
                "2. First time only: if there are no entity types or palette yet, use 'Create Default Entity Types' and 'Create Default Palette'.\n\n" +
                "AUTHOR A LEVEL BY HAND\n" +
                "3. Pick a base tool (e.g. Pin) and click grid cells to place pins.\n" +
                "4. Pick the 'Rope' tool, choose a color, click pins in order, then 'Finish Rope'. Make a few ropes that cross.\n" +
                "5. Use 'Flip Crossing' to set which rope is on top at a crossing.\n\n" +
                "GENERATE WITH AI (paste into any AI chat)\n" +
                "6. Set Difficulty, click '1 · Copy prompt'.\n" +
                "7. Paste it into an AI chat (Claude, Gemini, ChatGPT…), send, then copy the JSON answer.\n" +
                "8. Paste that JSON into the box and click '2 · Import JSON'.\n\n" +
                "CHECK & SAVE (always)\n" +
                "9. 'Validate' must be green — fix issues if not.\n" +
                "10. Set Level Id and click 'Save'.",
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

            _levelIdField = CompactIntField("Level ID", 1);
            bar.Add(_levelIdField);
            bar.Add(MakeButton("Load", () => LoadLevel(_levelIdField.value), Css.BtnPrimary));
            bar.Add(MakeButton("Save", SaveCurrentLevel, Css.BtnSave));
            bar.Add(MakeButton("Delete", () => DeleteLevel(_levelIdField.value), Css.BtnDanger));

            var sep = new VisualElement();
            sep.AddToClassList(Css.TopbarSep);
            bar.Add(sep);

            _widthField = CompactIntField("Width", 6);
            _heightField = CompactIntField("Height", 6);
            _timeField = CompactIntField("Time(s)", 45);
            bar.Add(_widthField);
            bar.Add(_heightField);
            bar.Add(_timeField);
            bar.Add(MakeButton("Generate Grid", GenerateGrid, Css.BtnPrimary));

            return bar;
        }

        private VisualElement BuildLevelPropsBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList(Css.LevelPropsBar);

            var diffLbl = new Label("Difficulty");
            diffLbl.AddToClassList(Css.LevelPropsBarLabel);
            bar.Add(diffLbl);

            _difficultyField = new EnumField(LevelDifficulty.Normal);
            _difficultyField.style.minWidth = 80;
            _difficultyField.style.marginRight = 12;
            _difficultyField.tooltip = "Auto-computed on Validate. Override manually before saving.";
            _difficultyField.RegisterValueChangedCallback(evt =>
            {
                if (_level != null) _level.Difficulty = (LevelDifficulty)evt.newValue;
            });
            bar.Add(_difficultyField);

            var lbl = new Label("Background");
            lbl.AddToClassList(Css.LevelPropsBarLabel);
            bar.Add(lbl);

            _bgMaterialField = new ObjectField { objectType = typeof(Material) };
            _bgMaterialField.AddToClassList(Css.LevelPropsBarField);
            _bgMaterialField.RegisterValueChangedCallback(evt =>
            {
                var mat = evt.newValue as Material;
                if (_level != null) _level.BackgroundMaterial = mat;
                ApplyBackgroundToCanvas(mat);
            });
            bar.Add(_bgMaterialField);

            var opacityLbl = new Label("Opacity");
            opacityLbl.AddToClassList(Css.LevelPropsBarLabel);
            opacityLbl.style.marginLeft = 12;
            bar.Add(opacityLbl);

            _bgOpacitySlider = new Slider(0f, 1f) { value = 1f };
            _bgOpacitySlider.style.width = 100;
            _bgOpacitySlider.style.marginLeft = 4;
            _bgOpacitySlider.RegisterValueChangedCallback(evt =>
            {
                if (_bgDimmerLayer != null)
                    _bgDimmerLayer.style.opacity = evt.newValue;
            });
            bar.Add(_bgOpacitySlider);

            var divider = new VisualElement();
            divider.style.width = 1;
            divider.style.alignSelf = Align.Stretch;
            divider.style.backgroundColor = EditorColors.Separator;
            divider.style.marginLeft = 12;
            divider.style.marginRight = 8;
            bar.Add(divider);

            var gridLbl = new Label("Grid");
            gridLbl.AddToClassList(Css.LevelPropsBarLabel);
            bar.Add(gridLbl);

            _gridColorField = new ColorField { showAlpha = true, value = EditorColors.GridDefault };
            _gridColorField.style.width = 80;
            _gridColorField.RegisterValueChangedCallback(evt =>
            {
                if (_canvas != null) _canvas.GridStrokeColor = evt.newValue;
                _canvas?.MarkDirtyRepaint();
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

            _bgDimmerLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _bgDimmerLayer.style.position = Position.Absolute;
            _bgDimmerLayer.style.top = 0;
            _bgDimmerLayer.style.left = 0;
            _bgDimmerLayer.style.width = Length.Percent(100);
            _bgDimmerLayer.style.height = Length.Percent(100);
            _bgDimmerLayer.style.backgroundColor = Color.clear;
            _canvasHost.Add(_bgDimmerLayer);

            _canvas = new RopeCanvasElement
            {
                PegColorResolver = ResolveEntityColor,
                MarkerResolver = typeId => _entityLookup.TryGetValue(typeId, out var def)
                    ? EditorDataFor(def)?.CanvasMarker ?? CanvasMarker.None
                    : CanvasMarker.None,
            };
            _canvas.AddToClassList(Css.Canvas);
            _canvas.CellClicked = OnCanvasCellClicked;
            _canvas.CellDragged = OnCanvasCellDragged;
            _canvas.Released = () => RefreshPanels();

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
            panel.style.width = 340f; // default — enough to show all content without clipping

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(Css.RightScroll);

            scroll.Add(BuildEditToolsSection());
            scroll.Add(BuildEntityPlacementSection());
            scroll.Add(BuildPaletteSection());
            scroll.Add(BuildEntityCreatorSection());
            scroll.Add(BuildHelpSection());

            panel.Add(scroll);
            return panel;
        }

        private VisualElement BuildEditToolsSection()
        {
            var s = MakeSection("Tools");
            _editToolsContainer = MakeRow();
            _editToolsContainer.AddToClassList(Css.RowWrap);
            s.Add(_editToolsContainer);
            return s;
        }

        private VisualElement BuildEntityPlacementSection()
        {
            var s = MakeSection("Entity Placement");
            _toolsContainer = MakeRow();
            _toolsContainer.AddToClassList(Css.RowWrap);
            RebuildToolbar();
            s.Add(_toolsContainer);

            var paintToggle = new Toggle("Paint on drag") { value = _isPainting };
            paintToggle.RegisterValueChangedCallback(e => _isPainting = e.newValue);
            s.Add(paintToggle);

            return s;
        }

        private void RebuildToolbar()
        {
            if (_toolsContainer == null) return;
            _toolsContainer.Clear();
            _toolButtons.Clear();
            _baseButtons.Clear();

            // Rope sits at sort order 50. Base types with SortOrder < 50 appear before it,
            // SortOrder >= 50 appear after. Ungrouped always last.
            const int RopeSortOrder = 50;
            var items = new List<(int order, string name, System.Action add)>();
            items.Add((RopeSortOrder, "Rope", () => AddToolButton(Tool.Rope, "Rope")));
            foreach (var b in _baseTypes)
            {
                var captured = b;
                items.Add((b.SortOrder, b.DisplayName,
                    () => AddBaseButton(captured, captured.DisplayName, captured.EditorColor)));
            }

            items.Sort((a, b) =>
            {
                int cmp = a.order.CompareTo(b.order);
                return cmp != 0 ? cmp : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
            foreach (var item in items) item.add();
            if (HasUngrouped())
                AddBaseButton(null, "Ungrouped", EditorColors.EntityFallback);

            if (_editToolsContainer != null)
            {
                _editToolsContainer.Clear();
                AddEditToolButton(Tool.Erase, "Erase");
                AddEditToolButton(Tool.Flip, "Flip Crossing");
            }

            UpdateToolActiveStates();
            UpdateShortcutHints();

            LevelEditorCommands.Refresh();
            BuildCommandTable();
        }

        private void AddToolButton(Tool tool, string label)
        {
            var btn = new Button(() => SetTool(tool)) { text = label };
            btn.AddToClassList(Css.Tool);
            _toolButtons[tool] = btn;
            _toolsContainer.Add(btn);
        }

        private void AddEditToolButton(Tool tool, string label)
        {
            var btn = new Button(() => SetTool(tool)) { text = label };
            btn.AddToClassList(Css.Tool);
            _toolButtons[tool] = btn;
            _editToolsContainer.Add(btn);
        }

        private void AddBaseButton(EntityBaseTypeSO baseType, string label, Color accent)
        {
            var btn = new Button(() =>
            {
                if (baseType != null && _tool == Tool.Place && _selectedBaseType == baseType)
                    EditorGUIUtility.PingObject(baseType);
                else
                    SelectBase(baseType);
            }) { text = label };
            btn.AddToClassList(Css.Tool);
            btn.style.borderLeftWidth = 6;
            btn.style.borderLeftColor = accent;
            if (baseType != null)
            {
                string baseShortcut = ShortcutTooltip(LevelEditorCommands.BaseCommandId(baseType.BaseId));
                btn.tooltip = string.IsNullOrEmpty(baseShortcut)
                    ? "Click again → locate in Project"
                    : $"{baseShortcut}\nClick again → locate in Project";
                btn.RegisterCallback<PointerEnterEvent>(_ => btn.style.opacity = 0.6f);
                btn.RegisterCallback<PointerLeaveEvent>(_ => btn.style.opacity = 1f);
            }

            _baseButtons.Add((baseType, btn));
            _toolsContainer.Add(btn);
        }

        private void SelectBase(EntityBaseTypeSO baseType)
        {
            _tool = Tool.Place;
            _selectedBaseType = baseType;
            _selectedEntity = SubTypesOf(baseType).FirstOrDefault();
            RebuildPalette();
            UpdateToolActiveStates();
            RefreshCanvas();
        }

        private void SelectEntity(EntityDefinitionSO def)
        {
            if (def == null) return;
            _tool = Tool.Place;
            _selectedBaseType = EditorDataFor(def)?.BaseType;
            _selectedEntity = def;
            RebuildPalette();
            UpdateToolActiveStates();
            RefreshCanvas();
        }

        private VisualElement BuildPaletteSection()
        {
            var s = MakeSection("Brush");
            _paletteContainer = new VisualElement();
            s.Add(_paletteContainer);
            return s;
        }

        private VisualElement BuildEntityCreatorSection()
        {
            var s = MakeSection("New entity type", expanded: false);

            var btn = new Button { text = "+ New Entity Type" };
            btn.AddToClassList(Css.Btn);
            btn.AddToClassList(Css.BtnPrimary);
            btn.clicked += () => UnityEditor.PopupWindow.Show(
                btn.worldBound, new EntityTypePopup(new List<EntityBaseTypeSO>(_baseTypes), TryCreateEntityType));
            s.Add(btn);
            return s;
        }

        private void ApplyBackgroundToCanvas(Material mat)
        {
            if (_canvasHost == null || _bgDimmerLayer == null) return;

            var effective = mat ?? EnvironmentSettings.DefaultBackgroundMaterial;

            if (effective == null)
            {
                _bgDimmerLayer.style.backgroundImage = StyleKeyword.None;
                _bgDimmerLayer.style.backgroundColor = Color.clear;
                _canvasHost.style.backgroundColor = EditorColors.CanvasBg;
                if (_canvas != null)
                {
                    _canvas.GridStrokeColor = EditorColors.GridDefault;
                    _canvas.RopeOutlineColor = EditorColors.RopeOutlineDark;
                }

                _gridColorField?.SetValueWithoutNotify(EditorColors.GridDefault);
                return;
            }

            var tex = ExtractTexture(effective);
            if (tex != null)
            {
                _bgDimmerLayer.style.backgroundImage = new StyleBackground(tex);
                _bgDimmerLayer.style.backgroundColor = Color.clear;
                _bgDimmerLayer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            else
            {
                Color bgColor = effective.HasProperty("_BaseColor") ? effective.GetColor("_BaseColor")
                    : effective.HasProperty("_Color") ? effective.GetColor("_Color")
                    : Color.gray;
                _bgDimmerLayer.style.backgroundImage = StyleKeyword.None;
                _bgDimmerLayer.style.backgroundColor = bgColor;
            }

            _canvasHost.style.backgroundColor = Color.clear;
            if (_canvas != null)
            {
                var derived = DeriveGridColor(effective);
                _canvas.GridStrokeColor = derived;
                _canvas.RopeOutlineColor = EditorColors.RopeOutlineLight;
                _gridColorField?.SetValueWithoutNotify(derived);
            }
        }

        private static Color DeriveGridColor(Material m)
        {
            Color bg = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                : m.HasProperty("_Color") ? m.GetColor("_Color")
                : Color.gray;
            float lum = bg.r * 0.2126f + bg.g * 0.7152f + bg.b * 0.0722f;
            return lum > 0.4f
                ? new Color(0f, 0f, 0f, 0.22f) // light background → dark grid lines
                : new Color(1f, 1f, 1f, 0.22f); // dark background  → light grid lines
        }

        private static Texture2D ExtractTexture(Material m)
        {
            if (m == null) return null;
            foreach (var name in m.GetTexturePropertyNames())
            {
                if (m.GetTexture(name) is Texture2D tex) return tex;
            }

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

        public void LoadGeneratedLevel(LevelDataSO level)
        {
            level.LevelId = _levelIdField.value;
            _level = level;
            _isEditMode = false;
            _currentLevelId = 0;
            _previewRope = null;
            _selectedRopeId = -1;
            _nextRopeId = _level.Ropes.Count == 0 ? 0 : _level.Ropes.Max(r => r.RopeId) + 1;
            _widthField.value = _level.GridWidth;
            _heightField.value = _level.GridHeight;
            _timeField.value = _level.TimeSeconds;
            RefreshAll();
            _bgMaterialField.SetValueWithoutNotify(_level.BackgroundMaterial);
            ApplyBackgroundToCanvas(_level.BackgroundMaterial);
        }

        public static bool IsNailed(EntityDefinitionSO def)
        {
            foreach (var tag in def.Tags)
                if (string.Equals(tag, "nailed", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "locked", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        #endregion

        #region UI: dynamic panels

        private void RebuildPalette()
        {
            _paletteContainer.Clear();
            _entityButtons.Clear();
            switch (_tool)
            {
                case Tool.Place: BuildPlacePalette(); break;
                case Tool.Rope: BuildRopePalette(); break;
                case Tool.Erase:
                    _paletteContainer.Add(new Label("Erase: left-click a node to remove its entity."));
                    break;
                case Tool.Flip:
                    _paletteContainer.Add(new Label("Flip: click near a crossing to swap which rope is on top."));
                    break;
            }
        }

        private void BuildPlacePalette()
        {
            if (_entityDefs.Count == 0)
            {
                _paletteContainer.Add(new HelpBox(
                    "No entity types yet. Use “+ New Entity Type” below (pick or create a base type), " +
                    "or click to create a starter Pin set.",
                    HelpBoxMessageType.Info));
                _paletteContainer.Add(MakeButton("Create Default Entity Types", CreateDefaultEntityTypes,
                    Css.BtnPrimary));
                return;
            }

            var subTypes = SubTypesOf(_selectedBaseType).ToList();
            string baseName = _selectedBaseType != null ? _selectedBaseType.DisplayName : "Ungrouped";

            if (subTypes.Count == 0)
            {
                _paletteContainer.Add(new HelpBox(
                    $"“{baseName}” has no sub-types yet. Add one with “+ New Entity Type” below.",
                    HelpBoxMessageType.Info));
                return;
            }

            var header = new Label(_selectedBaseType != null ? $"{baseName} types  ⤢" : $"{baseName} types");
            header.AddToClassList(Css.SectionHeader);
            if (_selectedBaseType != null)
            {
                header.tooltip = $"Click to locate {baseName} base asset in Project";
                header.RegisterCallback<PointerEnterEvent>(_ => header.style.opacity = 0.6f);
                header.RegisterCallback<PointerLeaveEvent>(_ => header.style.opacity = 1f);
                header.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(_selectedBaseType));
            }

            _paletteContainer.Add(header);

            var row = MakeRow();
            row.AddToClassList(Css.RowWrap);
            foreach (var def in subTypes)
            {
                var captured = def;
                var btn = new Button(() =>
                {
                    if (_selectedEntity == captured)
                        EditorGUIUtility.PingObject(captured);
                    else
                    {
                        _selectedEntity = captured;
                        RebuildPalette();
                        RefreshCanvas();
                    }
                })
                {
                    text = def.DisplayName
                };
                btn.AddToClassList(Css.Tool);
                if (def == _selectedEntity) btn.AddToClassList(Css.ToolActive);
                btn.style.borderLeftWidth = 6;
                btn.style.borderLeftColor = EditorDataFor(def)?.EditorColor ?? EditorColors.EntityFallback;
                string shortcut = ShortcutTooltip(LevelEditorCommands.EntityCommandId(def.TypeId));
                btn.tooltip = string.IsNullOrEmpty(shortcut)
                    ? "Click again → locate in Project"
                    : $"{shortcut}\nClick again → locate in Project";
                _entityButtons.Add((captured, btn));
                row.Add(btn);
            }

            _paletteContainer.Add(row);
        }

        private void BuildRopePalette()
        {
            if (_paletteAssets.Count == 0)
            {
                _paletteContainer.Add(MakeButton("Create Default Palette", CreateDefaultPalette, Css.BtnPrimary));
            }
            else
            {
                _selectedPaletteIndex = Mathf.Clamp(_selectedPaletteIndex, 0, _paletteAssets.Count - 1);
                var paletteNames = _paletteAssets.Select(p => p.DisplayName).ToList();

                var headerRow = MakeRow();
                headerRow.style.marginBottom = 2;
                var presetsLabel = new Label("Presets");
                presetsLabel.AddToClassList(Css.PalettePickerLabel);
                headerRow.Add(presetsLabel);

                var selector = new DropdownField(paletteNames, _selectedPaletteIndex);
                selector.AddToClassList(Css.PaletteSelectorCompact);
                selector.RegisterValueChangedCallback(e =>
                {
                    _selectedPaletteIndex = paletteNames.IndexOf(e.newValue);
                    _hiddenSwatchNames.Clear();
                    RebuildPalette();
                });
                headerRow.Add(selector);

                var palette = _paletteAssets[_selectedPaletteIndex];
                var filterBtn = new Button();
                filterBtn.text = "⚙";
                filterBtn.AddToClassList(Css.SwatchFilterBtn);
                filterBtn.tooltip = "Show / hide palette colors";
                filterBtn.clicked += () => UnityEditor.PopupWindow.Show(
                    filterBtn.worldBound,
                    new SwatchFilterPopup(palette, _hiddenSwatchNames, RebuildPalette));
                headerRow.Add(filterBtn);
                _paletteContainer.Add(headerRow);

                var swRow = new VisualElement();
                swRow.AddToClassList(Css.SwatchGrid);
                swRow.name = "swatchRow";
                foreach (var entry in palette.Entries)
                {
                    if (_hiddenSwatchNames.Contains(entry.Name)) continue;
                    var color = entry.Color;
                    var variant = entry.Variant;
                    var b = new Button(() =>
                    {
                        _ropeColor = color;
                        _ropeMaterial = variant;
                        if (_previewRope != null)
                        {
                            _previewRope.Tint = color;
                            _previewRope.Material = variant;
                        }
                        UpdateSwatchSelection();
                        RefreshCanvas();
                    }) { tooltip = entry.Name };
                    b.AddToClassList(Css.Swatch);
                    b.style.backgroundColor = color;
                    if (ColorApproxEqual(color, _ropeColor)) b.AddToClassList(Css.SwatchSelected);
                    swRow.Add(b);
                }

                _paletteContainer.Add(swRow);
            }

            var addBtn = new Button { text = "+ Add to Palette" };
            addBtn.AddToClassList(Css.Btn);
            addBtn.style.marginTop = 4;
            addBtn.clicked += () => UnityEditor.PopupWindow.Show(
                addBtn.worldBound,
                new PaletteColorPopup(new List<ColorPaletteSO>(_paletteAssets), _ropeColor, TryAddPaletteColor));
            _paletteContainer.Add(addBtn);

            var actionRow = MakeRow();
            actionRow.style.marginTop = 6;
            actionRow.Add(MakeButton("Finish Rope", FinishRope, Css.BtnSave));
            actionRow.Add(MakeButton("Cancel Rope", CancelRope, Css.BtnDanger));
            _paletteContainer.Add(actionRow);
        }

        private void RebuildRopeList()
        {
            _ropeListContainer.Clear();
            if (_level == null || _level.Ropes.Count == 0)
            {
                _ropeListContainer.Add(new Label("No ropes yet. Pick the Rope tool and click entities in order."));
                return;
            }

            foreach (var rope in _level.Ropes.OrderByDescending(r => r.Layer))
            {
                var captured = rope;

                var row = new VisualElement();
                row.AddToClassList(Css.RopeRow);
                if (rope.RopeId == _selectedRopeId) row.AddToClassList(Css.RopeRowSelected);

                var left = new VisualElement();
                left.AddToClassList(Css.RopeRowLeft);
                left.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedRopeId = captured.RopeId;
                    RefreshAll();
                });

                var handle = new Label("≡");
                handle.AddToClassList(Css.RopeRowHandle);
                left.Add(handle);

                var swatch = new VisualElement();
                swatch.AddToClassList(Css.RopeRowSwatch);
                swatch.style.backgroundColor = rope.Tint;
                if (_tool == Tool.Rope) swatch.AddToClassList(Css.RopeRowSwatchPaintable);
                swatch.tooltip = _tool == Tool.Rope ? "Click to apply active palette color" : string.Empty;
                swatch.RegisterCallback<ClickEvent>(e =>
                {
                    if (_tool != Tool.Rope) return;
                    e.StopPropagation(); // don't also trigger the row selection click
                    Undo.RecordObject(_level, "Recolor Rope");
                    captured.Tint = _ropeColor;
                    captured.Material = _ropeMaterial;
                    RefreshAll();
                });
                left.Add(swatch);

                var info = new VisualElement();
                info.AddToClassList(Css.RopeRowInfo);
                var nameLabel = new Label($"Rope {rope.RopeId + 1}");
                nameLabel.AddToClassList(Css.RopeRowName);
                var metaLabel = new Label($"{rope.Path.Count} pts");
                metaLabel.AddToClassList(Css.RopeRowMeta);
                info.Add(nameLabel);
                info.Add(metaLabel);
                left.Add(info);

                var badge = new Label($"L{rope.Layer}");
                badge.AddToClassList(Css.RopeRowBadge);
                left.Add(badge);

                row.Add(left);

                var actions = new VisualElement();
                actions.AddToClassList(Css.RopeRowActions);

                var frontBtn = new Button(() =>
                {
                    BringToFront(captured);
                    RefreshAll();
                }) { text = "↑", tooltip = "Bring to front" };
                frontBtn.AddToClassList(Css.RopeRowIconBtn);
                actions.Add(frontBtn);

                var backBtn = new Button(() =>
                {
                    SendToBack(captured);
                    RefreshAll();
                }) { text = "↓", tooltip = "Send to back" };
                backBtn.AddToClassList(Css.RopeRowIconBtn);
                actions.Add(backBtn);

                var deleteBtn = new Button(() =>
                {
                    DeleteRope(captured);
                    RefreshAll();
                }) { text = "✕", tooltip = "Delete rope" };
                deleteBtn.AddToClassList(Css.RopeRowIconBtn);
                deleteBtn.AddToClassList(Css.RopeRowIconBtnDanger);
                actions.Add(deleteBtn);

                row.Add(actions);
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

            var report = LevelValidator.Validate(_level, _entityLookup.Keys);

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
            AddMetricChip(metricsRow, m.EntityCount.ToString(), "entities");
            AddMetricChip(metricsRow, m.RopeCount.ToString(), "ropes");
            AddMetricChip(metricsRow, m.CrossingCount.ToString(), "crossings", m.CrossingCount > 0);
            AddMetricChip(metricsRow, m.TangleResidual.ToString(), "tangle", m.TangleResidual > 0);
            AddMetricChip(metricsRow, m.ColorCount.ToString(), "colors");
            AddMetricChip(metricsRow, m.OverrideCount.ToString(), "overrides");
            AddMetricChip(metricsRow, $"{m.TotalPathLength:0.0}", "length");
            AddMetricChip(metricsRow, $"{_level.TimeSeconds}s", "time");
            _validationContainer.Add(metricsRow);

            var diffRow = MakeRow();
            diffRow.style.marginTop = 4;
            var diffBadge = new Label($"{m.Difficulty}  ·  {m.DifficultyScore:0.0}");
            diffBadge.AddToClassList(Css.DifficultyBadge);
            diffBadge.AddToClassList($"{Css.DifficultyBadge}--{m.Difficulty}");
            diffRow.Add(diffBadge);
            _validationContainer.Add(diffRow);

            if (_difficultyField != null && _level != null)
            {
                _level.Difficulty = m.Difficulty;
                _difficultyField.SetValueWithoutNotify(m.Difficulty);
            }

            if (_validationStatusDot != null)
            {
                _validationStatusDot.EnableInClassList(Css.StatusDotOk, report.IsValid);
                _validationStatusDot.EnableInClassList(Css.StatusDotError, !report.IsValid);
                _validationStatusDot.EnableInClassList(Css.StatusDotWarn, false);
            }
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

        #endregion

        #region Canvas interaction

        private void OnCanvasCellClicked(int x, int y, Vector2 local, int button)
        {
            if (_level == null) return;
            var subCoord = new Vector2Int(x, y);
            var coarseCoord = new Vector2Int(x / CrossingSolver.SubDiv, y / CrossingSolver.SubDiv);
            var coord = coarseCoord;

            switch (_tool)
            {
                case Tool.Place:
                    if (button == 1) RemoveEntity(coord);
                    else PlaceEntity(coord);
                    break;
                case Tool.Erase:
                    RemoveEntity(coord);
                    break;
                case Tool.Rope:
                    if (button == 1) FinishRope();
                    else AddRopeWaypoint(subCoord);
                    break;
                case Tool.Flip:
                    FlipNearestCrossing(local);
                    break;
            }

            RefreshCanvas();
            if (_tool != Tool.Place && _tool != Tool.Erase) RefreshPanels();
        }

        private void OnCanvasCellDragged(int x, int y)
        {
            if (_level == null) return;
            var subCoord = new Vector2Int(x, y);
            var coarseCoord = new Vector2Int(x / CrossingSolver.SubDiv, y / CrossingSolver.SubDiv);
            var coord = coarseCoord;

            if (_tool == Tool.Rope && _previewRope != null)
            {
                AddRopeWaypoint(subCoord);
                RefreshCanvas();
                return;
            }

            if (!_isPainting) return;
            if (_tool == Tool.Place) PlaceEntity(coord);
            else if (_tool == Tool.Erase) RemoveEntity(coord);
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

            var defaultMat = EnvironmentSettings.DefaultBackgroundMaterial;
            _level.BackgroundMaterial = defaultMat;

            _isEditMode = false;
            _currentLevelId = 0;
            _nextRopeId = 0;
            _selectedRopeId = -1;
            _previewRope = null;

            RefreshAll();
            _bgMaterialField.SetValueWithoutNotify(defaultMat);
        }

        private void PlaceEntity(Vector2Int coord)
        {
            if (_selectedEntity == null) return;
            if (_level.GridEntities.Any(p => p.Coordinates == coord)) return;
            Undo.RecordObject(_level, "Place Entity");
            _level.GridEntities.Add(new GridEntityData(coord, _selectedEntity.TypeId));
        }

        private void RemoveEntity(Vector2Int coord)
        {
            if (_level == null) return;
            Undo.RecordObject(_level, "Remove Entity");
            _level.GridEntities.RemoveAll(p => p.Coordinates == coord);
            foreach (var rope in _level.Ropes)
            {
                if (rope?.Path == null) continue;
                for (int i = 1; i < rope.Path.Count - 1; i++)
                {
                    var wp = rope.Path[i];
                    if (wp.PegCoord != coord || wp.IsBendPoint) continue;
                    rope.Path[i] = new RopeWaypoint(wp.PegCoord, wp.Side, true);
                }
            }
        }

        private void AddRopeWaypoint(Vector2Int subCoord)
        {
            bool isFirstPoint = _previewRope == null;
            var coarseCoord = new Vector2Int(subCoord.x / CrossingSolver.SubDiv, subCoord.y / CrossingSolver.SubDiv);
            bool hasPeg = _level.GridEntities.FindIndex(e => e.Coordinates == coarseCoord) >= 0;

            bool connectingToPin = hasPeg &&
                                   (isFirstPoint || subCoord == CrossingSolver.PinToSub(coarseCoord));
            if (connectingToPin) subCoord = CrossingSolver.PinToSub(coarseCoord);

            if (isFirstPoint && !hasPeg) return;

            if (isFirstPoint)
            {
                int layer = _level.Ropes.Count == 0 ? 0 : _level.Ropes.Max(r => r.Layer) + 1;
                _previewRope = new RopeData(_nextRopeId, _ropeColor, layer) { Material = _ropeMaterial };
            }

            if (_previewRope.Path.Count > 0 && _previewRope.Path[^1].PegCoord == subCoord) return;

            if (_previewRope.Path.Count >= 5)
            {
                ShowNotification(new GUIContent("Max 3 bend points reached."));
                return;
            }

            if (_previewRope.Path.Count > 0)
            {
                Vector2Int last = _previewRope.Path[^1].PegCoord;
                int maxSubReach = MaxRopeReach * CrossingSolver.SubDiv;
                if (Mathf.Max(Mathf.Abs(subCoord.x - last.x), Mathf.Abs(subCoord.y - last.y)) > maxSubReach)
                {
                    ShowNotification(new GUIContent($"Too far — max reach is {MaxRopeReach} cells."));
                    return;
                }
            }

            _waypointHistory.Push(new List<RopeWaypoint>(_previewRope.Path));
            _previewRope.Path.Add(new RopeWaypoint(subCoord, WindSide.None, !connectingToPin));
        }

        private void UndoLastWaypoint()
        {
            if (_previewRope == null) return;
            if (_waypointHistory.Count == 0)
            {
                // İlk waypoint bile eklenmemişse rope'u iptal et.
                CancelRope();
                return;
            }

            _previewRope.Path = _waypointHistory.Pop();
            if (_previewRope.Path.Count == 0)
            {
                _previewRope = null;
                _waypointHistory.Clear();
            }

            RefreshCanvas();
        }

        private void FinishRope()
        {
            if (_previewRope == null) return;

            if (_previewRope.Path.Count < 2)
            {
                ShowNotification(new GUIContent("Connect to at least two pins before finishing."));
                return;
            }

            if (_previewRope.Path[^1].IsBendPoint)
            {
                ShowNotification(new GUIContent("End the rope on a pin."));
                return;
            }

            Undo.RecordObject(_level, "Add Rope");
            _level.Ropes.Add(_previewRope);
            _selectedRopeId = _previewRope.RopeId;
            _nextRopeId++;

            _previewRope = null;
            _waypointHistory.Clear();
            RefreshAll();
        }

        private void CancelRope()
        {
            _previewRope = null;
            _waypointHistory.Clear();
            RefreshAll();
        }

        private void DeleteRope(RopeData rope)
        {
            Undo.RecordObject(_level, "Delete Rope");
            _level.Ropes.Remove(rope);
            _level.CrossingOverrides.RemoveAll(o => o.RopeIdA == rope.RopeId || o.RopeIdB == rope.RopeId);
            if (_selectedRopeId == rope.RopeId) _selectedRopeId = -1;
        }

        private void BringToFront(RopeData rope)
        {
            if (_level.Ropes.Count <= 1) return;
            Undo.RecordObject(_level, "Rope To Front");
            rope.Layer = _level.Ropes.Max(r => r.Layer) + 1;
        }

        private void SendToBack(RopeData rope)
        {
            if (_level.Ropes.Count <= 1) return;
            Undo.RecordObject(_level, "Rope To Back");
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

            Undo.RecordObject(_level, "Flip Crossing");
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
            if (_difficultyField != null) _level.Difficulty = (LevelDifficulty)_difficultyField.value;
            var report = LevelValidator.Validate(_level, _entityLookup.Keys);
            RebuildValidation();

            if (!report.IsValid)
            {
                EditorUtility.DisplayDialog("Cannot save — level has errors",
                    string.Join("\n", report.Errors.Take(10)), "OK");
                return;
            }

            var saved = LevelSaveUtility.SaveLevel(_level, LevelEditorPaths.Levels);
            if (saved != null)
            {
                _isEditMode = true;
                _currentLevelId = _level.LevelId;
                EditorUtility.DisplayDialog("Saved", $"Level {_level.LevelId} saved.", "OK");
            }
        }

        private void LoadLevel(int id)
        {
            var asset = LevelSaveUtility.GetSelectedLevel(id, LevelEditorPaths.Levels);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Load", $"No level with id {id}.", "OK");
                return;
            }

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

            MigrateLevelToSubGrid();
            RefreshAll();
            _bgMaterialField.SetValueWithoutNotify(_level.BackgroundMaterial);
            _difficultyField?.SetValueWithoutNotify(_level.Difficulty);
            ApplyBackgroundToCanvas(_level.BackgroundMaterial);
        }

        private void MigrateLevelToSubGrid()
        {
            if (_level == null) return;
            // Check new-format first: a coarse coord can equal a sub-grid pin coord by coincidence,
            // causing a false positive that would scale coords again by SubDiv and corrupt the level.
            bool alreadyMigrated = _level.Ropes.Any(r =>
                r.Path.Count > 0 &&
                _level.GridEntities.Any(e => CrossingSolver.PinToSub(e.Coordinates) == r.Path[0].PegCoord));
            if (alreadyMigrated) return;

            bool isOldFormat = _level.Ropes.Any(r =>
                r.Path.Count > 0 &&
                _level.GridEntities.Any(e => e.Coordinates == r.Path[0].PegCoord));
            if (!isOldFormat) return;

            foreach (var rope in _level.Ropes)
                for (int i = 0; i < rope.Path.Count; i++)
                {
                    var wp = rope.Path[i];
                    rope.Path[i] = new RopeWaypoint(CrossingSolver.PinToSub(wp.PegCoord), wp.Side, wp.IsBendPoint);
                }

            EditorUtility.SetDirty(_level);
            Debug.Log("[LevelCreator] Migrated level to sub-grid coordinate system.");
        }

        private void DeleteLevel(int id)
        {
            if (!EditorUtility.DisplayDialog("Delete", $"Delete level {id}?", "Delete", "Cancel")) return;
            if (!LevelSaveUtility.DeleteSelectedLevel(id, LevelEditorPaths.Levels))
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
            RebuildPalette();
            UpdateToolActiveStates();
            RefreshCanvas();
        }

        private void RefreshAll()
        {
            RefreshCanvas();
            RebuildPalette();
            RefreshPanels();
            UpdateToolActiveStates();
        }

        private void UpdateToolActiveStates()
        {
            foreach (var kv in _toolButtons)
                kv.Value.EnableInClassList(Css.ToolActive, _tool == kv.Key);
            foreach (var (baseType, btn) in _baseButtons)
                btn.EnableInClassList(Css.ToolActive, _tool == Tool.Place && _selectedBaseType == baseType);
        }

        private void RefreshPanels()
        {
            RebuildRopeList();
            RebuildValidation();
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

        private void RefreshCanvas()
        {
            if (_canvas == null) return;
            _canvas.Level = _level;
            _canvas.GridWidth = _level?.GridWidth ?? 0;
            _canvas.GridHeight = _level?.GridHeight ?? 0;
            _canvas.PreviewRope = _previewRope;
            _canvas.SelectedRopeId = _selectedRopeId;
            _canvas.ShowCrossings = _tool == Tool.Flip;
            _canvas.ShowSubGrid = _tool == Tool.Rope;
            _canvas.Redraw();
            ApplyBackgroundToCanvas(_level?.BackgroundMaterial);
        }

        #endregion

        #region Keyboard shortcuts

        private void BuildCommandTable()
        {
            _commands.Clear();

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

            foreach (var b in _baseTypes)
            {
                var captured = b;
                _commands[LevelEditorCommands.BaseCommandId(b.BaseId)] = () => SelectBase(captured);
            }

            foreach (var def in _entityDefs)
            {
                var captured = def;
                _commands[LevelEditorCommands.EntityCommandId(def.TypeId)] = () => SelectEntity(captured);
            }
        }

        private void OnShortcutKeyDown(KeyDownEvent e)
        {
            var combo = KeyCombo.FromEvent(e);
            if (combo.IsEmpty) return;

            if (!combo.Ctrl && !combo.Alt && IsEditingText()) return;

            // Rope çizimi aktifken Ctrl+Z son waypoint'i geri alır (Unity undo'suna düşmez).
            if (_previewRope != null && combo.Ctrl && e.keyCode == KeyCode.Z)
            {
                UndoLastWaypoint();
                e.StopPropagation();
                return;
            }

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

        private void UpdateShortcutHints()
        {
            foreach (var kv in _toolButtons)
            {
                if (!ToolCommandIds.TryGetValue(kv.Key, out var id)) continue;
                kv.Value.tooltip = ShortcutTooltip(id);
            }

            foreach (var (def, btn) in _entityButtons)
                btn.tooltip = ShortcutTooltip(LevelEditorCommands.EntityCommandId(def.TypeId));

            foreach (var (baseType, btn) in _baseButtons)
            {
                if (baseType == null)
                {
                    btn.tooltip = string.Empty;
                    continue;
                }

                string sc = ShortcutTooltip(LevelEditorCommands.BaseCommandId(baseType.BaseId));
                btn.tooltip = string.IsNullOrEmpty(sc)
                    ? "Click again → locate in Project"
                    : $"{sc}\nClick again → locate in Project";
            }
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

        private void UpdateSwatchSelection()
        {
            var swRow = _paletteContainer.Q<VisualElement>("swatchRow");
            if (swRow == null) return;
            foreach (var child in swRow.Children())
            {
                var c = child.resolvedStyle.backgroundColor;
                bool sel = ColorApproxEqual(new Color(c.r, c.g, c.b, c.a), _ropeColor);
                child.EnableInClassList(Css.SwatchSelected, sel);
            }
        }

        private static bool ColorApproxEqual(Color a, Color b, float eps = 0.01f) =>
            Mathf.Abs(a.r - b.r) < eps && Mathf.Abs(a.g - b.g) < eps &&
            Mathf.Abs(a.b - b.b) < eps && Mathf.Abs(a.a - b.a) < eps;

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

    sealed class SwatchFilterPopup : UnityEditor.PopupWindowContent
    {
        private readonly ColorPaletteSO _palette;
        private readonly HashSet<string> _hidden;
        private readonly System.Action _onChanged;

        public SwatchFilterPopup(ColorPaletteSO palette, HashSet<string> hidden, System.Action onChanged)
        {
            _palette = palette;
            _hidden = hidden;
            _onChanged = onChanged;
        }

        private const float PopupWidth = 230f;
        private const float PopupMaxH = 260f;
        private const float ItemHeight = 28f;
        private const float HeaderHeight = 30f;

        public override Vector2 GetWindowSize()
        {
            float contentH = HeaderHeight + _palette.Entries.Count * ItemHeight + 8f;
            return new Vector2(PopupWidth, Mathf.Min(contentH, PopupMaxH));
        }

        public override void OnGUI(Rect rect)
        {
        }

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;
            root.style.paddingTop = 6;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 4;
            root.style.paddingBottom = 6;
            root.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            root.style.flexDirection = FlexDirection.Column;

            var title = new Label("Visible colors");
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            title.style.fontSize = 11;
            title.style.marginBottom = 4;
            title.style.flexShrink = 0;
            root.Add(title);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;

            foreach (var entry in _palette.Entries)
            {
                var name = entry.Name;
                var color = entry.Color;
                bool visible = !_hidden.Contains(name);

                var item = new VisualElement();
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.height = ItemHeight;
                item.style.paddingRight = 4;

                var swatch = new VisualElement();
                swatch.style.width = 16;
                swatch.style.height = 16;
                swatch.style.borderTopLeftRadius = 8;
                swatch.style.borderTopRightRadius = 8;
                swatch.style.borderBottomLeftRadius = 8;
                swatch.style.borderBottomRightRadius = 8;
                swatch.style.backgroundColor = color;
                swatch.style.borderTopWidth = 1;
                swatch.style.borderBottomWidth = 1;
                swatch.style.borderLeftWidth = 1;
                swatch.style.borderRightWidth = 1;
                swatch.style.borderTopColor = EditorColors.SwatchBorder;
                swatch.style.borderBottomColor = EditorColors.SwatchBorder;
                swatch.style.borderLeftColor = EditorColors.SwatchBorder;
                swatch.style.borderRightColor = EditorColors.SwatchBorder;
                swatch.style.marginRight = 6;
                swatch.style.flexShrink = 0;
                item.Add(swatch);

                var toggle = new Toggle { value = visible, text = name };
                toggle.style.flexGrow = 1;
                toggle.style.fontSize = 11;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _hidden.Remove(name);
                    else _hidden.Add(name);
                    _onChanged?.Invoke();
                });
                item.Add(toggle);
                scroll.Add(item);
            }

            root.Add(scroll);
        }
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