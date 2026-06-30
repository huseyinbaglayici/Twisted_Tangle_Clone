using TwistedTangle.Runtime.Data.Enums;
using UnityEngine;

namespace TwistedTangle.Editor.Settings
{
    [CreateAssetMenu(fileName = "DifficultySettings", menuName = "TwistedTangle/Difficulty Settings")]
    public class DifficultySettingsSO : ScriptableObject
    {
        [Header("Scoring Weights")]
        public float WCrossings = 1.0f;
        public float WRopes     = 0.5f;
        public float WColors    = 1.5f;
        public float WLength    = 0.2f;
        public float WOverrides = 1.0f;

        [Header("Thresholds")]
        [Tooltip("Score below this → Normal")]
        public float HardThreshold     = 24f;
        [Tooltip("Score below this → Hard; above → VeryHard")]
        public float VeryHardThreshold = 40f;

        // Tutorial and Special are never auto-assigned — manually curated only.
        public LevelDifficulty Classify(float score)
        {
            if (score < HardThreshold)     return LevelDifficulty.Normal;
            if (score < VeryHardThreshold) return LevelDifficulty.Hard;
            return LevelDifficulty.VeryHard;
        }

        public float ComputeScore(int crossings, int ropes, int colors, float length, int overrides) =>
            WCrossings * crossings + WRopes * ropes + WColors * colors + WLength * length + WOverrides * overrides;

        // Loads the asset from anywhere in the project, or creates it at the default path.
        public static DifficultySettingsSO LoadOrCreate()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:DifficultySettingsSO");
            if (guids.Length > 0)
                return UnityEditor.AssetDatabase.LoadAssetAtPath<DifficultySettingsSO>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));

            const string dir  = "Assets/TwistedTangle/EditorSettings";
            const string path = dir + "/DifficultySettings.asset";
            EnsureFolder("Assets/TwistedTangle");
            EnsureFolder(dir);
            var so = CreateInstance<DifficultySettingsSO>();
            UnityEditor.AssetDatabase.CreateAsset(so, path);
            UnityEditor.AssetDatabase.SaveAssets();
            return so;
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path[..slash];
            string name   = path[(slash + 1)..];
            EnsureFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, name);
        }
    }
}

