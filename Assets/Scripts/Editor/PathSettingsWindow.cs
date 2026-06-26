using System.IO;
using TwistedTangle.Editor.Utils;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Standalone window for editing every filesystem path the Level Creator uses — the folders levels,
    /// entities, base types and palettes are written to, plus the editor stylesheet. Each path is a Unity
    /// <see cref="ObjectField"/>: drag a folder/file in from the Project window, pick one via the object
    /// picker, or click the assigned asset (or "Reveal") to highlight it in the Project window. It only
    /// edits <see cref="LevelEditorPaths"/>, so the Level Creator need not be open (reopen it to re-apply
    /// a changed stylesheet).
    /// </summary>
    public class PathSettingsWindow : EditorWindow
    {
        private VisualElement _rowsHost;

        [MenuItem("TwistedTangle/Level Editor Paths")]
        public static void ShowWindow()
        {
            var w = GetWindow<PathSettingsWindow>();
            w.titleContent = new GUIContent("Tangle Paths");
            w.minSize = new Vector2(560, 360);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("tt-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = new Color(0.102f, 0.102f, 0.102f);

            var scroll = new ScrollView();
            scroll.AddToClassList("tt-right-scroll");
            scroll.style.flexGrow    = 1;
            scroll.style.paddingLeft  = 8;
            scroll.style.paddingRight = 8;
            scroll.style.paddingTop   = 8;

            var title = new Label("Twisted Tangle — Editor Paths");
            title.AddToClassList("tt-title");
            scroll.Add(title);

            scroll.Add(new HelpBox(
                "Where the Level Creator reads and writes its assets. Drag a folder/file from the Project " +
                "window onto a field, pick one with the object picker, or click an assigned asset to jump " +
                "to it in the Project window. Saved per-project on this machine.",
                HelpBoxMessageType.Info));

            _rowsHost = new VisualElement();
            scroll.Add(_rowsHost);

            var footer = MakeRow();
            footer.style.marginTop = 8;
            footer.Add(MakeButton("Reset All to Defaults", () =>
            {
                if (EditorUtility.DisplayDialog("Reset paths",
                        "Restore every Level Creator path to its default?", "Reset", "Cancel"))
                {
                    LevelEditorPaths.ResetAll();
                    Rebuild();
                }
            }, "tt-btn--danger"));
            scroll.Add(footer);

            root.Add(scroll);
            Rebuild();
        }

        private void Rebuild()
        {
            _rowsHost.Clear();
            foreach (var def in LevelEditorPaths.All)
                _rowsHost.Add(BuildRow(def));
        }

        private VisualElement BuildRow(LevelEditorPaths.PathDef def)
        {
            // One bordered section per path so the field, its path text and any warning stay grouped.
            var container = new VisualElement();
            container.AddToClassList("tt-section");

            var row = MakeRow();

            var name = new Label(def.DisplayName);
            name.style.minWidth = 130;
            row.Add(name);

            string current = LevelEditorPaths.Get(def.Id);

            // Folders are DefaultAssets in Unity; the stylesheet is a StyleSheet. The ObjectField then
            // accepts drag-drop from the Project window, offers a native picker, and pings on click.
            var objType = def.IsFolder ? typeof(DefaultAsset) : typeof(StyleSheet);
            var field = new ObjectField
            {
                objectType = objType,
                allowSceneObjects = false,
                value = AssetDatabase.LoadAssetAtPath(current, objType),
                tooltip = def.IsFolder
                    ? "Drag a folder here, or click it to reveal it in the Project window."
                    : "Drag a .uss file here, or click it to reveal it in the Project window."
            };
            field.style.flexGrow = 1;
            field.style.minWidth = 220;
            field.RegisterValueChangedCallback(e => OnObjectPicked(def, e.newValue));
            row.Add(field);

            var reveal = new Button(() => Reveal(current)) { text = "Reveal" };
            reveal.AddToClassList("tt-tool");
            reveal.tooltip = "Highlight this path in the Project window.";
            row.Add(reveal);

            var browse = new Button(() => Browse(def)) { text = "Browse…" };
            browse.AddToClassList("tt-tool");
            browse.tooltip = "Pick via the OS file dialog.";
            row.Add(browse);

            if (LevelEditorPaths.IsOverridden(def.Id))
            {
                var reset = new Button(() =>
                {
                    LevelEditorPaths.Reset(def.Id);
                    Rebuild();
                }) { text = "Default" };
                reset.AddToClassList("tt-tool");
                row.Add(reset);
            }

            container.Add(row);

            // The resolved project path, so designers can read/confirm it at a glance.
            var pathLabel = new Label(current);
            pathLabel.AddToClassList("tt-metric");
            pathLabel.style.marginLeft = 2;
            container.Add(pathLabel);

            var problem = Validate(def, current);
            if (problem != null)
            {
                var warn = new Label("⚠ " + problem);
                warn.AddToClassList("tt-validation__warn");
                container.Add(warn);
            }

            return container;
        }

        /// <summary>Applies a folder/file dropped or picked into the ObjectField (null clears to default).</summary>
        private void OnObjectPicked(LevelEditorPaths.PathDef def, Object picked)
        {
            if (picked == null)
            {
                LevelEditorPaths.Reset(def.Id);
                Rebuild();
                return;
            }

            string path = AssetDatabase.GetAssetPath(picked);
            if (def.IsFolder && !AssetDatabase.IsValidFolder(path))
            {
                EditorUtility.DisplayDialog("Pick a folder",
                    "This field needs a folder from the Project window.", "OK");
                Rebuild(); // revert the field to the stored value
                return;
            }

            LevelEditorPaths.Set(def.Id, path);
            Rebuild();
        }

        /// <summary>Selects and pings the asset at <paramref name="projectRelative"/> in the Project window.</summary>
        private static void Reveal(string projectRelative)
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>(projectRelative);
            if (obj == null)
            {
                EditorUtility.DisplayDialog("Not in project",
                    $"Nothing exists at \"{projectRelative}\" yet — it will be created on first use.", "OK");
                return;
            }

            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        /// <summary>Returns a human-readable note about the path, or null when it looks fine.</summary>
        private static string Validate(LevelEditorPaths.PathDef def, string path)
        {
            if (string.IsNullOrEmpty(path)) return "Path is empty.";
            if (path != "Assets" && !path.StartsWith("Assets/"))
                return "Should be project-relative (start with \"Assets/\").";

            if (def.IsFolder)
            {
                if (!AssetDatabase.IsValidFolder(path))
                    return "Folder doesn't exist yet — it will be created on first use.";
            }
            else if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                return "File not found at this path.";
            }

            return null;
        }

        private void Browse(LevelEditorPaths.PathDef def)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string startAbs = DirOrAssets(ToAbsolute(LevelEditorPaths.Get(def.Id), projectRoot));

            string picked = def.IsFolder
                ? EditorUtility.OpenFolderPanel($"Select {def.DisplayName}", startAbs, string.Empty)
                : EditorUtility.OpenFilePanel($"Select {def.DisplayName}", startAbs, def.FileExtension ?? string.Empty);

            if (string.IsNullOrEmpty(picked)) return;

            string rel = ToProjectRelative(picked);
            if (rel == null)
            {
                EditorUtility.DisplayDialog("Outside project",
                    "Pick a location inside this project's Assets folder.", "OK");
                return;
            }

            LevelEditorPaths.Set(def.Id, rel);
            Rebuild();
        }

        // --- path helpers ---

        private static string ToAbsolute(string projectRelative, string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRelative)) return Application.dataPath;
            return Path.Combine(projectRoot, projectRelative).Replace('\\', '/');
        }

        /// <summary>A directory the OS file panel can open from: the path itself, its parent, or Assets.</summary>
        private static string DirOrAssets(string absPath)
        {
            if (string.IsNullOrEmpty(absPath)) return Application.dataPath;
            if (Directory.Exists(absPath)) return absPath;
            var parent = Path.GetDirectoryName(absPath);
            return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : Application.dataPath;
        }

        /// <summary>Maps an absolute path inside the project back to "Assets/…"; null if it's outside.</summary>
        private static string ToProjectRelative(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            absolute = absolute.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/'); // ends with "/Assets"
            if (absolute == dataPath) return "Assets";
            if (absolute.StartsWith(dataPath + "/")) return "Assets" + absolute.Substring(dataPath.Length);
            return null;
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList("tt-row");
            r.AddToClassList("tt-row--wrap");
            return r;
        }

        private static Button MakeButton(string text, System.Action onClick, string ussClass)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("tt-btn");
            if (!string.IsNullOrEmpty(ussClass)) b.AddToClassList(ussClass);
            return b;
        }
    }
}
