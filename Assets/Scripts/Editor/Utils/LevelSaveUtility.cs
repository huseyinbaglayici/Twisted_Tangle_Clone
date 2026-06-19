using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    /// <summary>
    /// Persists levels as <see cref="LevelDataSO"/> assets named by id under a Resources folder, so
    /// the runtime can load them by id too. Save/load go through a deep copy in both directions so a
    /// save→close→reopen round-trip never loses or aliases data.
    /// </summary>
    public static class LevelSaveUtility
    {
        public static string PathFor(string folder, int levelId) => $"{folder}/Level_{levelId}.asset";

        /// <summary>
        /// Writes the working level to <c>folder/Level_&lt;id&gt;.asset</c>, creating or updating the
        /// asset in place (so existing references survive). Returns the on-disk asset, or null on error.
        /// </summary>
        public static LevelDataSO SaveLevel(LevelDataSO working, string folder)
        {
            if (working == null) return null;
            if (working.LevelId < 1)
            {
                Debug.LogError("[LevelSaveUtility] Cannot save: Level Id must be >= 1 (0 is reserved).");
                return null;
            }

            EnsureFolder(folder);
            string path = PathFor(folder, working.LevelId);
            var asset = AssetDatabase.LoadAssetAtPath<LevelDataSO>(path);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<LevelDataSO>();
                CopyInto(working, asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                CopyInto(working, asset);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        /// <summary>Loads the level asset for the given id, or null if it does not exist.</summary>
        public static LevelDataSO GetSelectedLevel(int levelId, string folder)
        {
            if (levelId < 0) return null;
            return AssetDatabase.LoadAssetAtPath<LevelDataSO>(PathFor(folder, levelId));
        }

        /// <summary>Deletes the level asset for the given id. Returns true if something was deleted.</summary>
        public static bool DeleteSelectedLevel(int levelId, string folder)
        {
            string path = PathFor(folder, levelId);
            if (AssetDatabase.LoadAssetAtPath<LevelDataSO>(path) == null) return false;
            bool ok = AssetDatabase.DeleteAsset(path);
            if (ok) AssetDatabase.Refresh();
            return ok;
        }

        /// <summary>Deep-copies all level data from <paramref name="src"/> into <paramref name="dst"/>.</summary>
        public static void CopyInto(LevelDataSO src, LevelDataSO dst)
        {
            dst.LevelId = src.LevelId;
            dst.GridWidth = src.GridWidth;
            dst.GridHeight = src.GridHeight;
            dst.TimeSeconds = src.TimeSeconds;

            dst.Pegs.Clear();
            dst.Pegs.AddRange(src.Pegs); // PegData is a value type — straight copy is a deep copy.

            dst.Ropes.Clear();
            foreach (var rope in src.Ropes)
                dst.Ropes.Add(CloneRope(rope));

            dst.CrossingOverrides.Clear();
            dst.CrossingOverrides.AddRange(src.CrossingOverrides); // value type
        }

        private static RopeData CloneRope(RopeData src)
        {
            var clone = new RopeData(src.RopeId, src.Tint, src.Layer)
            {
                Path = new List<RopeWaypoint>(src.Path) // RopeWaypoint is a value type
            };
            return clone;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0]; // expected to be "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
