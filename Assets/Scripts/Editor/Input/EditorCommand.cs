namespace Editor.Input
{
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