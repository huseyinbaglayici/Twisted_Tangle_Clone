using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    public static class LevelEditorPaths
    {
        // LevelFiles (not "Levels") so a stale override of the old LevelDataSO folder is ignored.
        public enum PathId
        {
            LevelFiles,
            Uss
        }

        public sealed class PathDef
        {
            public PathId Id;
            public string DisplayName;
            public string Default;
            public bool IsFolder;
            public string FileExtension;
        }

        public static readonly IReadOnlyList<PathDef> All = new[]
        {
            // Must stay under Resources/Levels: the runtime LevelRepository loads from there.
            new PathDef { Id = PathId.LevelFiles, DisplayName = "Level JSON folder", Default = "Assets/Resources/Levels", IsFolder = true },
            new PathDef { Id = PathId.Uss, DisplayName = "Editor stylesheet", Default = "Assets/Scripts/Editor/LevelCreator.uss", IsFolder = false, FileExtension = "uss" },
        };

        public static string Levels => Get(PathId.LevelFiles);
        public static string Uss => Get(PathId.Uss);

        private static readonly string PrefsKey =
            $"TwistedTangle.LevelEditor.Paths.{PlayerSettings.productGUID}";

        private static Dictionary<PathId, string> _overrides;

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

        public static string Get(PathId id)
        {
            if (Overrides.TryGetValue(id, out var path) && !string.IsNullOrEmpty(path)) return path;
            return GetDefault(id);
        }

        public static bool IsOverridden(PathId id) => Overrides.ContainsKey(id);

        public static void Set(PathId id, string path)
        {
            path = Normalize(path);
            if (string.IsNullOrEmpty(path) || string.Equals(path, GetDefault(id), StringComparison.Ordinal))
            {
                Reset(id);
                return;
            }

            Overrides[id] = path;
            Save();
        }

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

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            path = path.Trim().Replace('\\', '/');
            while (path.Length > 1 && path.EndsWith("/")) path = path[..^1];
            return path;
        }

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
