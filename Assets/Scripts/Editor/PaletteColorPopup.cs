using System;
using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Editor.Utils;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Transient form (anchored under "+ Add Color to Palette") for adding a named color entry to an
    /// existing palette asset, or optionally creating a new palette. Submits through a callback supplied
    /// by the owner: on success it closes, on failure it shows the error inline and stays open.
    /// </summary>
    public class PaletteColorPopup : PopupWindowContent
    {
        private const string NewPaletteChoice = "＋ New palette…";

        private readonly List<ColorPaletteSO> _palettes;
        private readonly Color _initialColor;
        // (existingPalette, newPaletteName, colorName, color) -> (ok, error). existingPalette null = create new.
        private readonly Func<ColorPaletteSO, string, string, Color, (bool ok, string error)> _onSubmit;

        private DropdownField _paletteDropdown;
        private TextField _newPaletteName;
        private TextField _colorName;
        private UnityEditor.UIElements.ColorField _colorField;
        private HelpBox _error;

        public PaletteColorPopup(List<ColorPaletteSO> palettes, Color initialColor,
            Func<ColorPaletteSO, string, string, Color, (bool ok, string error)> onSubmit)
        {
            _palettes = palettes ?? new List<ColorPaletteSO>();
            _initialColor = initialColor;
            _onSubmit = onSubmit;
        }

        public override Vector2 GetWindowSize() => new Vector2(320, 240);

        public override void OnGUI(Rect rect) { }

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            var title = new Label("Add color to palette");
            title.AddToClassList("tt-section__header");
            root.Add(title);

            var choices = _palettes.Select(p => p.name).ToList();
            choices.Add(NewPaletteChoice);
            _paletteDropdown = new DropdownField("Palette", choices, _palettes.Count > 0 ? 0 : choices.Count - 1)
            {
                tooltip = "Which palette asset to add this color to."
            };
            root.Add(_paletteDropdown);

            _newPaletteName = new TextField("New palette name");
            root.Add(_newPaletteName);

            _colorName = new TextField("Color name") { tooltip = "e.g. \"Teal\", \"Dark Red\"." };
            root.Add(_colorName);

            _colorField = new UnityEditor.UIElements.ColorField("Color") { value = _initialColor };
            root.Add(_colorField);

            _error = new HelpBox(string.Empty, HelpBoxMessageType.Error) { style = { display = DisplayStyle.None } };
            root.Add(_error);

            var addBtn = new Button(Submit) { text = "Add" };
            addBtn.AddToClassList("tt-btn");
            addBtn.AddToClassList("tt-btn--save");
            root.Add(addBtn);

            _paletteDropdown.RegisterValueChangedCallback(_ => UpdateNewPaletteVisibility());
            UpdateNewPaletteVisibility();

            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Submit();
                    e.StopPropagation();
                }
            });
            _colorName.schedule.Execute(() => _colorName.Focus());
        }

        private bool IsNewPaletteSelected() => _paletteDropdown.index >= _palettes.Count;

        private void UpdateNewPaletteVisibility() =>
            _newPaletteName.style.display = IsNewPaletteSelected() ? DisplayStyle.Flex : DisplayStyle.None;

        private void Submit()
        {
            ColorPaletteSO existing = IsNewPaletteSelected() ? null : _palettes[_paletteDropdown.index];
            string newPaletteName = IsNewPaletteSelected() ? _newPaletteName.value : null;

            var (ok, error) = _onSubmit(existing, newPaletteName, _colorName.value, _colorField.value);
            if (ok)
            {
                editorWindow.Close();
                return;
            }

            _error.text = error;
            _error.style.display = DisplayStyle.Flex;
        }
    }
}
