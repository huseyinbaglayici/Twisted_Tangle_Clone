using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
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
        }

        [SerializeField] private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;
    }
}
