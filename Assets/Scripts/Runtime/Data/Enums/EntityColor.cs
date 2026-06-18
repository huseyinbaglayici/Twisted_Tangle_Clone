using System.Collections.Generic;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.Enums
{
    /// <summary>
    /// Logical color identity used by ropes. Kept as an enum (not raw <see cref="Color"/>) so it
    /// round-trips cleanly through serialization and stays comparable for validation/metrics.
    /// </summary>
    public enum EntityColor
    {
        Default = 0,
        Red,
        Orange,
        Yellow,
        LightGreen,
        DarkGreen,
        LightBlue,
        DarkBlue,
        Purple,
        Pink,
        White
    }

    /// <summary>
    /// Single source of truth mapping <see cref="EntityColor"/> to a renderable <see cref="Color"/>.
    /// Shared by the editor canvas and the runtime loader so a saved level looks identical in both.
    /// </summary>
    public static class EntityColors
    {
        private static readonly Dictionary<EntityColor, Color> Lookup = new()
        {
            { EntityColor.Default, new Color(0.70f, 0.70f, 0.70f) },
            { EntityColor.Red, new Color(0.90f, 0.20f, 0.20f) },
            { EntityColor.Orange, new Color(1.00f, 0.55f, 0.00f) },
            { EntityColor.Yellow, new Color(0.95f, 0.85f, 0.10f) },
            { EntityColor.LightGreen, new Color(0.48f, 0.78f, 0.49f) },
            { EntityColor.DarkGreen, new Color(0.18f, 0.52f, 0.31f) },
            { EntityColor.LightBlue, new Color(0.36f, 0.78f, 0.96f) },
            { EntityColor.DarkBlue, new Color(0.14f, 0.34f, 0.62f) },
            { EntityColor.Purple, new Color(0.55f, 0.20f, 0.70f) },
            { EntityColor.Pink, new Color(0.95f, 0.35f, 0.70f) },
            { EntityColor.White, new Color(0.95f, 0.95f, 0.95f) }
        };

        public static Color Resolve(EntityColor color) =>
            Lookup.TryGetValue(color, out var c) ? c : Lookup[EntityColor.Default];
    }
}
