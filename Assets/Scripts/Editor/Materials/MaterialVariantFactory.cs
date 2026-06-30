using UnityEngine;

namespace TwistedTangle.Editor.Materials
{
    public interface IMaterialVariantFactory
    {
        /// <summary>A new material variant of <paramref name="template"/> with only the color overridden.</summary>
        Material Create(Material template, Color color);

        /// <summary>Re-applies just the color to an existing material (keeps its variant link).</summary>
        void ApplyColor(Material material, Color color);
    }

    public class MaterialVariantFactory : IMaterialVariantFactory
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Material Create(Material template, Color color)
        {
            if (template == null) return null;
            var variant = new Material(template) { parent = template }; // Material Variant
            ApplyColor(variant, color);
            return variant;
        }

        public void ApplyColor(Material material, Color color)
        {
            if (material == null) return;
            material.color = color;                                                        // shader's [MainColor]
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);  // URP/TCP
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);          // built-in/legacy
        }
    }
}
