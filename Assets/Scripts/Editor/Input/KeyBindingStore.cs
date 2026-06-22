using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Input
{
    /// <summary>
    /// Persists the user's Level Creator key bindings. Only overrides are stored — a command with no
    /// override falls back to its <see cref="EditorCommand.Default"/>. Saved per-project in EditorPrefs
    /// as JSON, so shortcuts stay machine-local (a personal preference) and never touch the asset
    /// database. <see cref="Changed"/> lets open windows refresh when a binding is edited.
    /// </summary>
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

        // Scope the prefs key by project so two projects on the same machine don't share bindings.
        private static readonly string PrefsKey =
            $"TwistedTangle.LevelEditor.KeyBindings.{PlayerSettings.productGUID}";

        private static Dictionary<string, KeyCombo> _overrides;

        /// <summary>Raised after any binding is set, cleared, or reset.</summary>
        public static event Action Changed;

        private static Dictionary<string, KeyCombo> Overrides
        {
            get
            {
                if (_overrides == null) Load();
                return _overrides;
            }
        }

        /// <summary>The effective combo for a command: the user's override if set, else its default.</summary>
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

        /// <summary>Removes the override, reverting the command to its default combo.</summary>
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

        /// <summary>The command whose effective combo equals <paramref name="combo"/>, or null. Skips empty combos.</summary>
        public static string FindCommandFor(KeyCombo combo)
        {
            if (combo.IsEmpty) return null;
            foreach (var cmd in LevelEditorCommands.All)
                if (Get(cmd.Id).Equals(combo)) return cmd.Id;
            return null;
        }

        /// <summary>Another command already using <paramref name="combo"/> (excluding <paramref name="exceptId"/>), or null.</summary>
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
                // Corrupt prefs blob — start from an empty set rather than throwing on window open.
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
