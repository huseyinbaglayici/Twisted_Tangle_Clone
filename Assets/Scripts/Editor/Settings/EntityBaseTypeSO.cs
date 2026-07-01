using UnityEngine;

namespace TwistedTangle.Editor.Settings
{
    [CreateAssetMenu(
        fileName = "EntityBase_New",
        menuName = "TwistedTangle/Entity Base Type",
        order = 0)]
    public class EntityBaseTypeSO : ScriptableObject
    {
        [SerializeField] private string baseId;
        [SerializeField] private string displayName;
        [SerializeField] private Color editorColor = new(0.85f, 0.85f, 0.85f);
        [SerializeField] private int sortOrder = 0;

        public string BaseId      => string.IsNullOrEmpty(baseId)      ? name   : baseId;
        public string DisplayName => string.IsNullOrEmpty(displayName)  ? BaseId : displayName;
        public Color  EditorColor => editorColor;
        public int    SortOrder   => sortOrder;
    }
}
