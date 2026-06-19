using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Small transient form, anchored under the "New Entity Type" button, for authoring a new
    /// EntityDefinitionSO without leaving the level editor. It submits through a callback supplied by
    /// the owner: on success it closes itself, on failure it shows the error inline and stays open.
    /// </summary>
    public class EntityTypePopup : PopupWindowContent
    {
        private const string UssPath = "Assets/Scripts/Editor/LevelCreator.uss";

        // Returns (ok, error). error is null/empty on success.
        private readonly Func<string, string, Color, GameObject, (bool ok, string error)> _onSubmit;

        private TextField _id, _name;
        private UnityEditor.UIElements.ColorField _color;
        private UnityEditor.UIElements.ObjectField _prefab;
        private HelpBox _error;

        public EntityTypePopup(Func<string, string, Color, GameObject, (bool ok, string error)> onSubmit)
        {
            _onSubmit = onSubmit;
        }

        public override Vector2 GetWindowSize() => new Vector2(340, 244);

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

            _id = new TextField("Type id")
            {
                tooltip = "Stable id stored in saved levels (e.g. \"lock\"). Must be unique and not change later."
            };
            _name = new TextField("Display name");
            _color = new UnityEditor.UIElements.ColorField("Editor color") { value = new Color(0.85f, 0.85f, 0.85f) };
            _prefab = new UnityEditor.UIElements.ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = "Prefab the runtime loader instantiates for this entity. Optional for the editor."
            };

            root.Add(_id);
            root.Add(_name);
            root.Add(_color);
            root.Add(_prefab);

            _error = new HelpBox(string.Empty, HelpBoxMessageType.Error) { style = { display = DisplayStyle.None } };
            root.Add(_error);

            var create = new Button(Submit) { text = "Create" };
            create.AddToClassList("tt-btn");
            create.AddToClassList("tt-btn--save");
            root.Add(create);

            // Enter submits; focus the first field once layout is ready.
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Submit();
                    e.StopPropagation();
                }
            });
            _id.schedule.Execute(() => _id.Focus());
        }

        private void Submit()
        {
            var (ok, error) = _onSubmit(_id.value, _name.value, _color.value, _prefab.value as GameObject);
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
