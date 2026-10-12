using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Editor.Input
{
    public static class KeyBindingStore
    {
        [Serializable]
        private struct Entry
        {
            public string Id;
            public KeyCombo Combo;
        }

        [Serializable]
        private class Data
        {
            public List<Entry> Entries = new();
        }

        private static readonly string PrefsKey =
            $"TwistedTangle.LevelEditor.KeyBindings.{PlayerSettings.productGUID}";

        private static Dictionary<string, KeyCombo> _overrides;

        public static event Action Changed;

        private static Dictionary<string, KeyCombo> Overrides
        {
            get
            {
                if (_overrides == null) Load();
                return _overrides;
            }
        }

        public static KeyCombo Get(string id)
        {
            if (Overrides.TryGetValue(id, out var combo)) return combo;
            return LevelEditorCommands.Find(id)?.Default ?? KeyCombo.None;
        }

        public static bool IsOverridden(string id) => Overrides.ContainsKey(id);

        public static void Set(string id, KeyCombo combo)
        {
            // Binding a command to its own default isn't an override — drop any existing one instead of
            // storing a redundant copy, so the row doesn't show a "Default" button that reverts to the same key.
            var def = LevelEditorCommands.Find(id)?.Default ?? KeyCombo.None;
            if (combo.Equals(def))
                Overrides.Remove(id);
            else
                Overrides[id] = combo;
            Save();
        }

        public static void Reset(string id)
        {
            if (Overrides.Remove(id)) Save();
        }

        public static void ResetAll()
        {
            if (Overrides.Count == 0) return;
            Overrides.Clear();
            Save();
        }

        public static string FindCommandFor(KeyCombo combo)
        {
            if (combo.IsEmpty) return null;
            foreach (var cmd in LevelEditorCommands.All)
                if (Get(cmd.Id).Equals(combo))
                    return cmd.Id;
            return null;
        }

        public static string FindConflict(KeyCombo combo, string exceptId)
        {
            if (combo.IsEmpty) return null;
            foreach (var cmd in LevelEditorCommands.All)
            {
                if (cmd.Id == exceptId) continue;
                if (Get(cmd.Id).Equals(combo)) return cmd.Id;
            }

            return null;
        }

        private static void Load()
        {
            _overrides = new Dictionary<string, KeyCombo>();
            var json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var data = JsonUtility.FromJson<Data>(json);
                if (data?.Entries != null)
                    foreach (var e in data.Entries)
                        _overrides[e.Id] = e.Combo;
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
                data.Entries.Add(new Entry { Id = kv.Key, Combo = kv.Value });

            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            Changed?.Invoke();
        }
    }
}