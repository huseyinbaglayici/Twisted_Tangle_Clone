namespace TwistedTangle.Runtime.Data.Enums
{
    /// <summary>
    /// Which side of a peg a rope winds around as its path passes through that peg.
    /// <see cref="None"/> means the rope goes straight to the peg center (no wrap authored yet).
    /// </summary>
    public enum WindSide
    {
        None = 0,
        Left,
        Right
    }
}
