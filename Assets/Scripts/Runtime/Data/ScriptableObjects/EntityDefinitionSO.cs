using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    /// <summary>
    /// Data-driven definition of an entity type — anything that can sit on a grid node (a pin, a lock,
    /// whatever we invent next). Adding a new kind means creating a new asset of this type, no editor
    /// code changes. The editor palette and the runtime loader both discover these by scanning the
    /// project/Resources, so a new asset shows up automatically in the tool and loads correctly in game.
    /// </summary>
    /// <remarks>Renamed from <c>PegDefinitionSO</c>; <see cref="MovedFromAttribute"/> keeps assets and
    /// levels that referenced the old class name linked to this one.</remarks>
    [MovedFrom(true, sourceClassName: "PegDefinitionSO")]
    [CreateAssetMenu(
        fileName = "Entity_New",
        menuName = "TwistedTangle/Entity Definition",
        order = 0)]
    public class EntityDefinitionSO : ScriptableObject
    {
        [Tooltip("Stable identity referenced by saved levels. Must be unique and must NOT change " +
                 "once levels reference it, or those levels lose this entity type.")]
        [SerializeField] private string typeId;

        [Tooltip("Human-readable name shown on the palette button.")]
        [SerializeField] private string displayName;

        [Tooltip("Color used to draw this entity in the editor canvas.")]
        [SerializeField] private Color editorColor = new(0.85f, 0.85f, 0.85f);

        [Tooltip("The base type this entity belongs to (Pin, Lock, …). Sub-types are grouped under their " +
                 "base in the toolbar/palette. Left empty = \"Ungrouped\" until assigned.")]
        [SerializeField] private EntityBaseTypeSO baseType;

        [Tooltip("Optional prefab instantiated by the runtime loader. Leave empty to fall back to " +
                 "a generated placeholder.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Free-form tags for future behavior wiring (e.g. \"locked\", \"nailed\"). The " +
                 "editor does not interpret these, so new behaviors need no editor changes.")]
        [SerializeField] private string[] tags;

        public string TypeId      => string.IsNullOrEmpty(typeId)      ? name   : typeId;
        public string DisplayName => string.IsNullOrEmpty(displayName)  ? TypeId : displayName;
        public Color  EditorColor => editorColor;
        public EntityBaseTypeSO BaseType => baseType;
        public GameObject Prefab => prefab;
        public string[] Tags     => tags ?? System.Array.Empty<string>();
    }
}
