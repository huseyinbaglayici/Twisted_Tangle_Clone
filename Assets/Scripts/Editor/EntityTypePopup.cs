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
    public class EntityTypePopup : PopupWindowContent
    {
        private const string NewBaseChoice = "＋ New base type…";

        private const string PrefBase  = "TwistedTangle.EntityPopup.BaseIdx";
        private const string PrefSub   = "TwistedTangle.EntityPopup.SubName";
        private const string PrefColor = "TwistedTangle.EntityPopup.Color";

        private readonly List<EntityBaseTypeSO> _bases;

        // existingBase null = create new base
        private readonly Func<EntityBaseTypeSO, string, string, Color, GameObject, (bool ok, string error)> _onSubmit;

        private DropdownField _baseDropdown;
        private TextField _newBaseName, _subName;
        private UnityEditor.UIElements.ColorField _color;
        private UnityEditor.UIElements.ObjectField _prefab;
        private HelpBox _error;

        public EntityTypePopup(List<EntityBaseTypeSO> bases,
            Func<EntityBaseTypeSO, string, string, Color, GameObject, (bool ok, string error)> onSubmit)
        {
            _bases = bases ?? new List<EntityBaseTypeSO>();
            _onSubmit = onSubmit;
        }

        public override Vector2 GetWindowSize() => new Vector2(360, 238);

        // UI is built with UI Toolkit in OnOpen; IMGUI hook stays empty.
        public override void OnGUI(Rect rect)
        {
        }

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            var title = new Label("New entity type");
            title.AddToClassList(Css.SectionHeader);
            root.Add(title);

            // Restore saved base index (clamp to valid range)
            int savedBase = EditorPrefs.GetInt(PrefBase, 0);
            var choices = _bases.Select(b => b.DisplayName).ToList();
            choices.Add(NewBaseChoice);
            int safeIdx = Mathf.Clamp(savedBase, 0, choices.Count - 1);

            _baseDropdown = new DropdownField("Base type", choices, safeIdx)
            {
                tooltip = "Which base this sub-type belongs to (Pin, Lock, …). Choose ＋ New base type… to add one."
            };
            root.Add(_baseDropdown);

            _newBaseName = new TextField("New base name");
            root.Add(_newBaseName);

            _subName = new TextField("Sub-type name")
            {
                value = EditorPrefs.GetString(PrefSub, string.Empty),
                tooltip = "e.g. \"Standard\", \"Nailed\", \"Explodeable\"."
            };
            root.Add(_subName);

            // Restore saved color
            Color savedColor = EditorColors.PinDefault;
            string savedHex = EditorPrefs.GetString(PrefColor, string.Empty);
            if (!string.IsNullOrEmpty(savedHex))
                ColorUtility.TryParseHtmlString("#" + savedHex, out savedColor);

            _color = new UnityEditor.UIElements.ColorField("Editor color") { value = savedColor };
            root.Add(_color);

            _prefab = new UnityEditor.UIElements.ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = "Prefab the runtime loader instantiates for this entity. Optional for the editor."
            };
            root.Add(_prefab);

            _error = new HelpBox(string.Empty, HelpBoxMessageType.Error) { style = { display = DisplayStyle.None } };
            root.Add(_error);

            var create = new Button(Submit) { text = "Create" };
            create.AddToClassList(Css.Btn);
            create.AddToClassList(Css.BtnSave);
            create.style.marginTop = 4;
            root.Add(create);

            root.Add(BuildResetFooter());

            _baseDropdown.RegisterValueChangedCallback(_ => UpdateNewBaseVisibility());
            UpdateNewBaseVisibility();

            // Enter submits; focus the sub-type field once layout is ready.
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Submit();
                    e.StopPropagation();
                }
            });
            _subName.schedule.Execute(() => _subName.Focus());
        }

        public override void OnClose() => SavePrefs();

        private VisualElement BuildResetFooter()
        {
            var footer = new VisualElement();
            footer.style.flexShrink      = 0;
            footer.style.flexDirection   = FlexDirection.Row;
            footer.style.alignItems      = Align.Center;
            footer.style.justifyContent  = Justify.SpaceBetween;
            footer.style.paddingLeft     = 0;
            footer.style.paddingRight    = 0;
            footer.style.paddingTop      = 6;
            footer.style.paddingBottom   = 0;
            footer.style.marginTop       = 4;
            footer.style.borderTopWidth  = 1;
            footer.style.borderTopColor  = EditorColors.FooterBorder;

            var hint = new Label("Reset form fields");
            hint.style.fontSize = 11;
            hint.style.color    = EditorColors.HintText;
            footer.Add(hint);

            var btn = new Button(ResetPrefs) { text = "Reset", tooltip = "Clear saved form state and restore defaults." };
            btn.AddToClassList(Css.Btn);
            btn.AddToClassList(Css.BtnDanger);
            footer.Add(btn);

            return footer;
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void SavePrefs()
        {
            EditorPrefs.SetInt(PrefBase, _baseDropdown?.index ?? 0);
            EditorPrefs.SetString(PrefSub, _subName?.value ?? string.Empty);
            EditorPrefs.SetString(PrefColor,
                _color != null ? ColorUtility.ToHtmlStringRGB(_color.value) : string.Empty);
        }

        private void ResetPrefs()
        {
            EditorPrefs.DeleteKey(PrefBase);
            EditorPrefs.DeleteKey(PrefSub);
            EditorPrefs.DeleteKey(PrefColor);

            // Apply defaults to live fields immediately
            if (_baseDropdown != null) _baseDropdown.index = _bases.Count > 0 ? 0 : 0;
            if (_subName != null) _subName.value = string.Empty;
            if (_color != null) _color.value = EditorColors.PinDefault;
            if (_error != null) _error.style.display = DisplayStyle.None;
        }

        // ── UI logic ──────────────────────────────────────────────────────────

        private bool IsNewBaseSelected() => _baseDropdown.index >= _bases.Count;

        private void UpdateNewBaseVisibility() =>
            _newBaseName.style.display = IsNewBaseSelected() ? DisplayStyle.Flex : DisplayStyle.None;

        private void Submit()
        {
            SavePrefs(); // persist current state before potential close
            EntityBaseTypeSO existingBase = IsNewBaseSelected() ? null : _bases[_baseDropdown.index];
            string newBaseName = IsNewBaseSelected() ? _newBaseName.value : null;

            var (ok, error) = _onSubmit(existingBase, newBaseName, _subName.value, _color.value,
                _prefab.value as GameObject);
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