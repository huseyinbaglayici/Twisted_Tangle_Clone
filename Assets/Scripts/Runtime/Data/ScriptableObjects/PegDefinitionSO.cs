using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    /// <summary>
    /// Data-driven definition of a peg type. Adding a new peg kind (locked, nailed, whatever we
    /// invent next) means creating a new asset of this type — no editor code changes. The editor
    /// palette and the runtime registry both discover these by scanning the project/Resources, so
    /// a new asset shows up automatically in the tool and loads correctly in game.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Peg_New",
        menuName = "TwistedTangle/Peg Definition",
        order = 0)]
    public class PegDefinitionSO : ScriptableObject
    {
        [Tooltip("Stable identity referenced by saved levels. Must be unique and must NOT change " +
                 "once levels reference it, or those levels lose this peg type.")]
        [SerializeField] private string typeId;

        [Tooltip("Human-readable name shown on the palette button.")]
        [SerializeField] private string displayName;

        [Tooltip("Color used to draw this peg in the editor canvas.")]
        [SerializeField] private Color editorColor = new(0.85f, 0.85f, 0.85f);

        [Tooltip("Optional prefab instantiated by the runtime loader. Leave empty to fall back to " +
                 "a generated placeholder.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Free-form tags for future behavior wiring (e.g. \"locked\", \"nailed\"). The " +
                 "editor does not interpret these, so new behaviors need no editor changes.")]
        [SerializeField] private string[] tags;

        /// <summary>Stable id; falls back to the asset name if the field was left blank.</summary>
        public string TypeId => string.IsNullOrEmpty(typeId) ? name : typeId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? TypeId : displayName;
        public Color EditorColor => editorColor;
        public GameObject Prefab => prefab;
        public string[] Tags => tags ?? System.Array.Empty<string>();
    }
}
