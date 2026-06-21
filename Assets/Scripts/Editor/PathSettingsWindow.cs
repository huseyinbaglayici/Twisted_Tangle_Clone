using System.IO;
using TwistedTangle.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Standalone window for editing every filesystem path the Level Creator uses — the folders levels,
    /// entities, base types and palettes are written to, plus the editor stylesheet. It only edits
    /// <see cref="LevelEditorPaths"/>, so the Level Creator need not be open; changes are picked up the
    /// next time a path is used (reopen the Level Creator to re-apply a changed stylesheet).
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

            var scroll = new ScrollView();
            scroll.AddToClassList("tt-main-scroll");

            var title = new Label("Twisted Tangle — Editor Paths");
            title.AddToClassList("tt-title");
            scroll.Add(title);

            scroll.Add(new HelpBox(
                "Where the Level Creator reads and writes its assets. Paths are project-relative " +
                "(must start with \"Assets/\"). Saved per-project on this machine.",
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
            // One bordered section per path so the value field and its warning stay visually grouped.
            var container = new VisualElement();
            container.AddToClassList("tt-section");

            var row = MakeRow();

            var name = new Label(def.DisplayName);
            name.style.minWidth = 130;
            row.Add(name);

            string current = LevelEditorPaths.Get(def.Id);

            // isDelayed: commit on Enter/blur rather than every keystroke (each commit rebuilds the row).
            var field = new TextField { value = current, isDelayed = true };
            field.style.flexGrow = 1;
            field.style.minWidth = 220;
            field.RegisterValueChangedCallback(e =>
            {
                LevelEditorPaths.Set(def.Id, e.newValue);
                Rebuild();
            });
            row.Add(field);

            var browse = new Button(() => Browse(def)) { text = "Browse…" };
            browse.AddToClassList("tt-tool");
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

            var problem = Validate(def, current);
            if (problem != null)
            {
                var warn = new Label("⚠ " + problem);
                warn.AddToClassList("tt-validation__warn");
                container.Add(warn);
            }

            return container;
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
