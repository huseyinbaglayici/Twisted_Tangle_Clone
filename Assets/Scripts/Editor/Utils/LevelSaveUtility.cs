using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    public static class LevelSaveUtility
    {
        public static string PathFor(string folder, int levelId) => $"{folder}/Level_{levelId}.asset";

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

        public static LevelDataSO GetSelectedLevel(int levelId, string folder)
        {
            if (levelId < 0) return null;
            return AssetDatabase.LoadAssetAtPath<LevelDataSO>(PathFor(folder, levelId));
        }

        public static bool DeleteSelectedLevel(int levelId, string folder)
        {
            string path = PathFor(folder, levelId);
            if (AssetDatabase.LoadAssetAtPath<LevelDataSO>(path) == null) return false;
            bool ok = AssetDatabase.DeleteAsset(path);
            if (ok) AssetDatabase.Refresh();
            return ok;
        }

        public static void CopyInto(LevelDataSO src, LevelDataSO dst)
        {
            dst.LevelId = src.LevelId;
            dst.Difficulty = src.Difficulty;
            dst.GridWidth = src.GridWidth;
            dst.GridHeight = src.GridHeight;
            dst.TimeSeconds = src.TimeSeconds;

            dst.GridEntities.Clear();
            dst.GridEntities.AddRange(src.GridEntities);

            dst.Ropes.Clear();
            foreach (var rope in src.Ropes)
                dst.Ropes.Add(CloneRope(rope));

            dst.CrossingOverrides.Clear();
            dst.CrossingOverrides.AddRange(src.CrossingOverrides);

            dst.BackgroundMaterial = src.BackgroundMaterial;
        }

        private static RopeData CloneRope(RopeData src) =>
            new(src.RopeId, src.Tint, src.Layer)
            {
                Material = src.Material,
                Path = new List<RopeWaypoint>(src.Path)
            };

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
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