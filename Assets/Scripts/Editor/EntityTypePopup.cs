using System;
using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Transient form (anchored under "+ New Entity Type") for authoring a new entity sub-type. You pick an
    /// existing base type from the dropdown — or "＋ New base type…" to create one inline — then name the
    /// sub-type. It submits through a callback supplied by the owner: on success it closes, on failure it
    /// shows the error inline and stays open.
    /// </summary>
    public class EntityTypePopup : PopupWindowContent
    {
        private const string UssPath = "Assets/Scripts/Editor/LevelCreator.uss";
        private const string NewBaseChoice = "＋ New base type…";

        private readonly List<EntityBaseTypeSO> _bases;
        // (existingBase, newBaseName, subName, color, prefab) -> (ok, error). existingBase null = create a new base.
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

        public override Vector2 GetWindowSize() => new Vector2(360, 300);

        // UI is built with UI Toolkit in OnOpen; IMGUI hook stays empty.
        public override void OnGUI(Rect rect) { }

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            var title = new Label("New entity type");
            title.AddToClassList("tt-section__header");
            root.Add(title);

            var choices = _bases.Select(b => b.DisplayName).ToList();
            choices.Add(NewBaseChoice);
            _baseDropdown = new DropdownField("Base type", choices, _bases.Count > 0 ? 0 : choices.Count - 1)
            {
                tooltip = "Which base this sub-type belongs to (Pin, Lock, …). Choose “＋ New base type…” to add one."
            };
            root.Add(_baseDropdown);

            _newBaseName = new TextField("New base name");
            root.Add(_newBaseName);

            _subName = new TextField("Sub-type name")
            {
                tooltip = "e.g. \"Standard\", \"Nailed\", \"Explodeable\"."
            };
            root.Add(_subName);

            _color = new UnityEditor.UIElements.ColorField("Editor color") { value = new Color(0.85f, 0.85f, 0.85f) };
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
            create.AddToClassList("tt-btn");
            create.AddToClassList("tt-btn--save");
            root.Add(create);

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

        private bool IsNewBaseSelected() => _baseDropdown.index >= _bases.Count;

        private void UpdateNewBaseVisibility() =>
            _newBaseName.style.display = IsNewBaseSelected() ? DisplayStyle.Flex : DisplayStyle.None;

        private void Submit()
        {
            EntityBaseTypeSO existingBase = IsNewBaseSelected() ? null : _bases[_baseDropdown.index];
            string newBaseName = IsNewBaseSelected() ? _newBaseName.value : null;

            var (ok, error) = _onSubmit(existingBase, newBaseName, _subName.value, _color.value, _prefab.value as GameObject);
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
