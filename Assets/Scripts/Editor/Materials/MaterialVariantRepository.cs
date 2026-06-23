using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Materials
{
    /// <summary>
    /// Persistence layer for color-variant materials: owns the asset path + folder, de-duplicates
    /// (reuse the existing asset for a template+color and just keep its color in sync), and writes to
    /// disk. Object construction is delegated to <see cref="IMaterialVariantFactory"/>, so this class
    /// only deals with "where/how to store", not "how to build".
    /// </summary>
    public class MaterialVariantRepository
    {
        private readonly string _folder;
        private readonly IMaterialVariantFactory _factory;

        public MaterialVariantRepository(string folder, IMaterialVariantFactory factory)
        {
            _folder = folder;
            _factory = factory;
        }

        /// <summary>Returns the saved variant asset for (template, color), creating it if missing.</summary>
        public Material GetOrCreate(Material template, Color color)
        {
            if (template == null) return null;
            EnsureFolder(_folder);

            string paletteName = _folder.Contains('/') ? _folder[(_folder.LastIndexOf('/') + 1)..] : _folder;
            string path = $"{_folder}/{paletteName}_{ColorUtility.ToHtmlStringRGB(color)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                _factory.ApplyColor(existing, color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var created = _factory.Create(template, color);
            if (created != null) AssetDatabase.CreateAsset(created, path);
            return created;
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
