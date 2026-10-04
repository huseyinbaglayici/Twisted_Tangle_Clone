using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    /// <summary>
    /// Editable, central registry of every filesystem path the Level Creator depends on — the level JSON
    /// folder (shared with the runtime) and the editor stylesheet. Defaults
    /// reproduce the original hard-coded layout; any override is saved per-project in EditorPrefs
    /// (machine-local, like key bindings) so changing a path never dirties the asset database.
    /// <see cref="Changed"/> lets open windows react when a path is edited.
    /// </summary>
    public static class LevelEditorPaths
    {
        // LevelFiles (not "Levels") so a stale override of the old LevelDataSO folder is ignored.
        public enum PathId
        {
            LevelFiles,
            Uss
        }

        /// <summary>Static metadata for one configurable path: its label, default and what it points at.</summary>
        public sealed class PathDef
        {
            public PathId Id;
            public string DisplayName;
            public string Default;
            public bool IsFolder;        // true → folder picker; false → file picker
            public string FileExtension; // file picker filter (e.g. "uss"); unused for folders
        }

        public static readonly IReadOnlyList<PathDef> All = new[]
        {
            // Must stay under Resources/Levels: the runtime LevelRepository loads from there.
            new PathDef { Id = PathId.LevelFiles, DisplayName = "Level JSON folder", Default = "Assets/Resources/Levels", IsFolder = true },
            new PathDef { Id = PathId.Uss, DisplayName = "Editor stylesheet", Default = "Assets/Scripts/Editor/LevelCreator.uss", IsFolder = false, FileExtension = "uss" },
        };

        // Convenience accessors so call sites read like the old consts.
        public static string Levels => Get(PathId.LevelFiles);
        public static string Uss => Get(PathId.Uss);

        // Scope the prefs key by project so two projects on the same machine don't share paths.
        private static readonly string PrefsKey =
            $"TwistedTangle.LevelEditor.Paths.{PlayerSettings.productGUID}";

        private static Dictionary<PathId, string> _overrides;

        /// <summary>Raised after any path is set or reset.</summary>
        public static event Action Changed;

        private static Dictionary<PathId, string> Overrides
        {
            get
            {
                if (_overrides == null) Load();
                return _overrides;
            }
        }

        public static PathDef Find(PathId id)
        {
            foreach (var d in All)
                if (d.Id == id)
                    return d;
            return null;
        }

        public static string GetDefault(PathId id) => Find(id)?.Default ?? string.Empty;

        /// <summary>The effective path: the user's override if set, else the default.</summary>
        public static string Get(PathId id)
        {
            if (Overrides.TryGetValue(id, out var path) && !string.IsNullOrEmpty(path)) return path;
            return GetDefault(id);
        }

        public static bool IsOverridden(PathId id) => Overrides.ContainsKey(id);

        public static void Set(PathId id, string path)
        {
            path = Normalize(path);
            // An empty value or one equal to the default clears the override, keeping prefs tidy.
            if (string.IsNullOrEmpty(path) || string.Equals(path, GetDefault(id), StringComparison.Ordinal))
            {
                Reset(id);
                return;
            }

            Overrides[id] = path;
            Save();
        }

        /// <summary>Removes the override, reverting the path to its default.</summary>
        public static void Reset(PathId id)
        {
            if (Overrides.Remove(id)) Save();
        }

        public static void ResetAll()
        {
            if (Overrides.Count == 0) return;
            Overrides.Clear();
            Save();
        }

        /// <summary>Trims, forward-slashes and drops any trailing slash — leaves project-relative form intact.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            path = path.Trim().Replace('\\', '/');
            while (path.Length > 1 && path.EndsWith("/")) path = path[..^1];
            return path;
        }

        // --- persistence (EditorPrefs JSON, per project) ---

        [Serializable]
        private struct Entry
        {
            public string Id;
            public string Path;
        }

        [Serializable]
        private class Data
        {
            public List<Entry> Entries = new();
        }

        private static void Load()
        {
            _overrides = new Dictionary<PathId, string>();
            var json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var data = JsonUtility.FromJson<Data>(json);
                if (data?.Entries != null)
                    foreach (var e in data.Entries)
                        if (Enum.TryParse(e.Id, out PathId id))
                            _overrides[id] = e.Path;
            }
            catch
            {
                // Corrupt prefs blob — fall back to defaults rather than throwing on window open.
                _overrides.Clear();
            }
        }

        private static void Save()
        {
            var data = new Data();
            foreach (var kv in _overrides)
                data.Entries.Add(new Entry { Id = kv.Key.ToString(), Path = kv.Value });

            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            Changed?.Invoke();
        }
    }
}
