using System;

namespace TwistedTangle.Runtime.Data.ValueObjects
{
    /// <summary>
    /// Marks a single rope crossing whose over/under order is flipped from the layer-based default.
    /// </summary>
    /// <remarks>
    /// The crossing identity is the pair of segments that cross, keyed by stable rope ids plus the
    /// segment index within each rope (segment i spans waypoints i..i+1). It is NOT keyed by pixel
    /// position, so the override survives moving/zooming/reopening the level. The pair is stored in
    /// a canonical order (lower rope id first) so a crossing has exactly one identity regardless of
    /// which rope is considered first — this is what keeps the control consistent even when many
    /// ropes pile onto the same spot.
    /// </remarks>
    [Serializable]
    public struct CrossingOverride : IEquatable<CrossingOverride>
    {
        public int RopeIdA;
        public int SegA;
        public int RopeIdB;
        public int SegB;

        public static CrossingOverride Create(int ropeIdA, int segA, int ropeIdB, int segB)
        {
            // Canonicalize so (A,B) and (B,A) collapse to the same key.
            bool aFirst = ropeIdA < ropeIdB || (ropeIdA == ropeIdB && segA <= segB);
            return aFirst
                ? new CrossingOverride { RopeIdA = ropeIdA, SegA = segA, RopeIdB = ropeIdB, SegB = segB }
                : new CrossingOverride { RopeIdA = ropeIdB, SegA = segB, RopeIdB = ropeIdA, SegB = segA };
        }

        public bool Equals(CrossingOverride other) =>
            RopeIdA == other.RopeIdA && SegA == other.SegA &&
            RopeIdB == other.RopeIdB && SegB == other.SegB;

        public override bool Equals(object obj) => obj is CrossingOverride o && Equals(o);

        public override int GetHashCode() => unchecked((((RopeIdA * 397) ^ SegA) * 397 ^ RopeIdB) * 397 ^ SegB);
    }
}
