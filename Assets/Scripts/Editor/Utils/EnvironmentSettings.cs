using System;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    public static class EnvironmentSettings
    {
        private static readonly string Key =
            $"TwistedTangle.Env.{PlayerSettings.productGUID}";

        public static event Action Changed;

        public static Material DefaultBackgroundMaterial
        {
            get
            {
                var path = EditorPrefs.GetString(Key + ".BgMat", string.Empty);
                if (string.IsNullOrEmpty(path)) return null;
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            set
            {
                var path = value != null ? AssetDatabase.GetAssetPath(value) : string.Empty;
                EditorPrefs.SetString(Key + ".BgMat", path);
                Changed?.Invoke();
            }
        }
    }
}
