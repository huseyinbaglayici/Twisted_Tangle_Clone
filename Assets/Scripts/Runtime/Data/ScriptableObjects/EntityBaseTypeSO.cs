using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    /// <summary>
    /// Data-driven definition of an entity <b>base type</b> — a top-level placeable kind (Pin, Lock, …)
    /// that appears as its own tool button. Each base groups one or more <see cref="EntityDefinitionSO"/>
    /// sub-types (e.g. Pin → Standard, Nailed). Adding a new base kind means creating a new asset of this
    /// type, no editor code changes; the toolbar discovers them by scanning the project/Resources.
    /// </summary>
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

        [Tooltip("Show the ⊘ occupied marker on canvas cells of this type. Enable for Obstacle bases.")]
        [SerializeField] private bool showOccupiedMarker = false;
#endif

        public string BaseId      => string.IsNullOrEmpty(baseId)      ? name   : baseId;
        public string DisplayName => string.IsNullOrEmpty(displayName)  ? BaseId : displayName;
        public Color  EditorColor => editorColor;

#if UNITY_EDITOR
        public int  SortOrder           => sortOrder;
        public bool ShowOccupiedMarker  => showOccupiedMarker;
#endif
    }
}
