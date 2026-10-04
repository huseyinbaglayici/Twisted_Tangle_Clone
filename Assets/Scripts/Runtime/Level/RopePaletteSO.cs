using UnityEngine;

namespace Runtime.Level
{
    // Rope colors indexed by RopeJson.colorIndex. Shared by the runtime (LevelBuilder) and the Level Creator,
    // so a level looks the same in both. Levels store only an index, never a color.
    [CreateAssetMenu(fileName = "RopePalette", menuName = "TwistedTangle/Rope Palette", order = 2)]
    public class RopePaletteSO : ScriptableObject
    {
        [SerializeField] private Color[] colors =
        {
            new(0.90f, 0.20f, 0.20f), new(0.20f, 0.55f, 0.95f), new(0.30f, 0.75f, 0.35f), new(0.95f, 0.85f, 0.10f),
            new(0.55f, 0.25f, 0.80f), new(1.00f, 0.55f, 0.00f), new(0.95f, 0.40f, 0.70f), new(1.00f, 1.00f, 1.00f),
        };

        public int Count => colors?.Length ?? 0;

        public Color Get(int colorIndex)
        {
            if (Count == 0) return Color.white;
            int i = colorIndex % Count;
            return colors[i < 0 ? i + Count : i];
        }
    }
}
