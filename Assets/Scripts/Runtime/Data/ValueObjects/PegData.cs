using System;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ValueObjects
{
    /// <summary>
    /// A single peg placed on the grid. <see cref="TypeId"/> references a PegDefinitionSO by its
    /// stable id, which is what makes peg types data-driven: the editor never hard-codes a type.
    /// </summary>
    [Serializable]
    public struct PegData
    {
        public Vector2Int Coordinates;
        public string TypeId;

        public PegData(Vector2Int coordinates, string typeId)
        {
            Coordinates = coordinates;
            TypeId = typeId;
        }
    }
}
