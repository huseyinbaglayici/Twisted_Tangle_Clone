using TwistedTangle.Editor.Materials;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Inspectors
{
    /// <summary>
    /// Adds a "Generate Material Variants" button to the ColorPaletteSO inspector. It takes the
    /// assigned template (your ToonyColorsPro material) and produces one color-overriding variant asset
    /// per palette entry, wiring each into the entry's <c>Variant</c> field. The game then just reads
    /// those materials — no runtime material creation.
    /// </summary>
    [CustomEditor(typeof(ColorPaletteSO))]
    public class ColorPaletteSOEditor : UnityEditor.Editor
    {
        private const string MaterialsFolder = "Assets/Art/Materials/Game";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var palette = (ColorPaletteSO)target;
            using (new EditorGUI.DisabledScope(palette.VariantTemplate == null))
            {
                if (GUILayout.Button("Generate Material Variants (from template)", GUILayout.Height(28)))
                    GenerateVariants();
            }

            if (palette.VariantTemplate == null)
                EditorGUILayout.HelpBox(
                    "Assign a Variant Template (your ToonyColorsPro material) to generate per-color variants.",
                    MessageType.Info);
        }

        private void GenerateVariants()
        {
            var so = serializedObject;
            so.Update();

            var template = so.FindProperty("variantTemplate").objectReferenceValue as Material;
            if (template == null) return;

            // Factory builds the material; repository decides where/how it's stored.
            var repository = new MaterialVariantRepository(MaterialsFolder, new MaterialVariantFactory());
            var entries = so.FindProperty("entries");

            int created = 0;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                Color color = entry.FindPropertyRelative("Color").colorValue;
                var variant = repository.GetOrCreate(template, color);
                entry.FindPropertyRelative("Variant").objectReferenceValue = variant;
                if (variant != null) created++;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Material Variants",
                $"Generated/updated {created} variant(s) in {MaterialsFolder}.", "OK");
        }
    }
}
