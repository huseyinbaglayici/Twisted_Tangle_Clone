namespace TwistedTangle.Editor.Input
{
    /// <summary>
    /// Metadata for one bindable Level Creator action: a stable id (used as the persistence key and to
    /// look up the runtime callback), a display name and category for the bindings window, and the
    /// out-of-the-box default shortcut. The actual callback lives in the window that owns the action.
    /// </summary>
    public sealed class EditorCommand
    {
        public readonly string Id;
        public readonly string Category;
        public readonly string DisplayName;
        public readonly KeyCombo Default;

        public EditorCommand(string id, string category, string displayName, KeyCombo defaultCombo)
        {
            Id = id;
            Category = category;
            DisplayName = displayName;
            Default = defaultCombo;
        }
    }
}
