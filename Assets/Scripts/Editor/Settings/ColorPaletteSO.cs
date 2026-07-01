using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwistedTangle.Editor.Settings
{
    /// <summary>
    /// A designer-editable palette of named colors. Drives the rope color swatches in the editor so
    /// nothing is hard-coded: edit/add colors in the asset and they show up in the tool. Multiple
    /// palette assets are allowed; the editor aggregates them all.
    /// </summary>
    [CreateAssetMenu(fileName = "ColorPalette", menuName = "TwistedTangle/Color Palette", order = 2)]
    public class ColorPaletteSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Name;
            public Color Color;

            [Tooltip("Pre-generated material for this color (a variant of the template). Filled in by " +
                     "the 'Generate Material Variants' button. The game reads this — no runtime material creation.")]
            public Material Variant;
        }

        [Tooltip("Display name shown in the Level Creator palette selector. Defaults to the asset file name if left empty.")]
        [SerializeField] private string displayName;

        [Tooltip("The ToonyColorsPro (or any) material used as the parent/template. Generated entries " +
                 "become variants of this, overriding only the color, so the look stays in one place.")]
        [SerializeField] private Material variantTemplate;

        [SerializeField] private List<Entry> entries = new();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public Material VariantTemplate => variantTemplate;
        public IReadOnlyList<Entry> Entries => entries;
    }
}
