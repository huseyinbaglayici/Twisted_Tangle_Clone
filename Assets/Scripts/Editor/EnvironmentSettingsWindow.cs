using TwistedTangle.Editor.Utils;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    public class EnvironmentSettingsWindow : EditorWindow
    {
        [MenuItem("TwistedTangle/Environment Settings")]
        public static void ShowWindow()
        {
            var w = GetWindow<EnvironmentSettingsWindow>();
            w.titleContent = new GUIContent("Tangle — Environment");
            w.minSize = new Vector2(420, 140);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = new Color(0.102f, 0.102f, 0.102f);
            root.style.paddingTop    = 8;
            root.style.paddingLeft   = 12;
            root.style.paddingRight  = 12;
            root.style.paddingBottom = 8;

            var title = new Label("Environment Settings");
            title.AddToClassList("tt-title");
            root.Add(title);

            root.Add(new HelpBox(
                "These are project-wide defaults, saved on this machine (EditorPrefs). " +
                "Per-level overrides set in the Level Creator's Background field take priority.",
                HelpBoxMessageType.Info));

            var section = new VisualElement();
            section.AddToClassList("tt-section");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 4;

            var lbl = new Label("Default Background");
            lbl.style.minWidth = 150;
            row.Add(lbl);

            var field = new ObjectField { objectType = typeof(Material), allowSceneObjects = false };
            field.style.flexGrow = 1;
            field.SetValueWithoutNotify(EnvironmentSettings.DefaultBackgroundMaterial);
            field.RegisterValueChangedCallback(evt =>
                EnvironmentSettings.DefaultBackgroundMaterial = evt.newValue as Material);
            row.Add(field);

            section.Add(row);
            root.Add(section);
        }
    }
}
