using System.Collections.Generic;
using System.IO;
using Runtime.Level;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    /// <summary>
    /// Reads/writes the level JSON files the runtime loads (Resources/Levels/level_NNN.json).
    /// The editor has no separate level asset: what you save here is exactly what the game plays.
    /// </summary>
    public static class LevelFileUtility
    {
        public static string PathFor(int levelNumber) =>
            $"{LevelEditorPaths.Levels}/{LevelRepository.FileName(levelNumber)}.json";

        public static bool Exists(int levelNumber) => File.Exists(PathFor(levelNumber));

        public static LevelJson Load(int levelNumber)
        {
            string path = PathFor(levelNumber);
            if (!File.Exists(path)) return null;
            var level = LevelJson.FromJson(File.ReadAllText(path));
            if (level != null && level.levelNumber == 0) level.levelNumber = levelNumber;
            return level;
        }

        public static bool Save(LevelJson level)
        {
            if (level == null) return false;
            if (level.levelNumber < 1)
            {
                Debug.LogError("[LevelFileUtility] Cannot save: level number must be >= 1.");
                return false;
            }

            EnsureFolder(LevelEditorPaths.Levels);
            string path = PathFor(level.levelNumber);
            File.WriteAllText(path, level.ToJson());
            AssetDatabase.ImportAsset(path);
            return true;
        }

        public static bool Delete(int levelNumber)
        {
            string path = PathFor(levelNumber);
            return File.Exists(path) && AssetDatabase.DeleteAsset(path);
        }

        /// <summary>Level numbers of every level_NNN.json in the folder, ascending.</summary>
        public static List<int> ListLevelNumbers()
        {
            var result = new List<int>();
            string folder = LevelEditorPaths.Levels;
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "level_*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name.Substring("level_".Length), out int n)) result.Add(n);
            }
            result.Sort();
            return result;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
