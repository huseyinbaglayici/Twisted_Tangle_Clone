using System;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ValueObjects
{
    /// <summary>
    /// A single entity placed on a grid node. <see cref="TypeId"/> references an EntityDefinitionSO by
    /// its stable id, which is what makes entity types data-driven: the editor never hard-codes a type.
    /// </summary>
    [Serializable]
    public struct GridEntityData
    {
        public Vector2Int Coordinates;
        public string TypeId;

        public GridEntityData(Vector2Int coordinates, string typeId)
        {
            Coordinates = coordinates;
            TypeId = typeId;
        }
    }
}
