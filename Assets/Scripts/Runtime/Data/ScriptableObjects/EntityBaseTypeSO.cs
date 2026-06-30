using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    [CreateAssetMenu(
        fileName = "EntityBase_New",
        menuName = "TwistedTangle/Entity Base Type",
        order = 0)]
    public class EntityBaseTypeSO : ScriptableObject
    {
        [Tooltip("Stable identity for this base type. Must be unique and must NOT change once sub-types " +
                 "reference it.")]
        [SerializeField] private string baseId;

        [Tooltip("Human-readable name shown on the toolbar button.")]
        [SerializeField] private string displayName;

        [Tooltip("Accent color for the toolbar button so bases are easy to tell apart in the editor.")]
        [SerializeField] private Color editorColor = new(0.85f, 0.85f, 0.85f);

#if UNITY_EDITOR
        [Tooltip("Toolbar position. Rope is fixed at 50. Values < 50 appear before Rope, values > 50 after. Ties broken alphabetically.")]
        [SerializeField] private int sortOrder = 0;

#endif

        public string BaseId      => string.IsNullOrEmpty(baseId)      ? name   : baseId;
        public string DisplayName => string.IsNullOrEmpty(displayName)  ? BaseId : displayName;
        public Color  EditorColor => editorColor;

#if UNITY_EDITOR
        public int SortOrder => sortOrder;
#endif
    }
}
