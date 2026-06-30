using System;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ValueObjects
{
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
