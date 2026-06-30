namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
#if UNITY_EDITOR
    public enum CanvasMarker
    {
        None,    // no marker drawn
        Blocked, // ⊘  circle + diagonal
        Funnel,  // △  upward triangle
    }
#endif
}
