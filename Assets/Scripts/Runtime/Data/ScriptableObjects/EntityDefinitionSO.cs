using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    // Renamed from PegDefinitionSO; MovedFrom keeps saved levels linked after the rename.
    [MovedFrom(true, sourceClassName: "PegDefinitionSO")]
    [CreateAssetMenu(fileName = "Entity_New", menuName = "TwistedTangle/Entity Definition", order = 0)]
    public class EntityDefinitionSO : ScriptableObject
    {
        [SerializeField] private string typeId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private string[] tags;

        public string     TypeId      => string.IsNullOrEmpty(typeId)      ? name   : typeId;
        public string     DisplayName => string.IsNullOrEmpty(displayName) ? TypeId : displayName;
        public GameObject Prefab      => prefab;
        public string[]   Tags        => tags ?? System.Array.Empty<string>();
    }
}
