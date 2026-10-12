using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    internal static class EditorColors
    {
        public static readonly Color WindowBg       = new(0.102f, 0.102f, 0.102f);
        public static readonly Color CanvasBg       = new(0.067f, 0.067f, 0.067f);

        public static readonly Color Separator      = new(1f, 1f, 1f, 0.08f);
        public static readonly Color FooterBorder   = new(1f, 1f, 1f, 0.10f);
        public static readonly Color HintText       = new(1f, 1f, 1f, 0.45f);

        public static readonly Color GridDefault    = new(1f, 1f, 1f, 0.18f);

        public static readonly Color RopeOutlineDark  = new(1f,    1f,    1f,    0.72f);
        public static readonly Color RopeOutlineLight = new(0.06f, 0.06f, 0.06f, 0.60f);
        public static readonly Color SelectionGlow    = new(1f,    1f,    1f,    0.40f);

        public static readonly Color PegShadow      = new(0.06f, 0.06f, 0.06f, 0.88f);
        public static readonly Color PinDefault     = new(0.85f, 0.85f, 0.85f);
    }
}
