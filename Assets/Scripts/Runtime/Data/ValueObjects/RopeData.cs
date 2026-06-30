using System;
using System.Collections.Generic;
using TwistedTangle.Runtime.Data.Enums;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ValueObjects
{
    /// <summary>
    /// One waypoint of a rope path. Either an endpoint/pin waypoint (sits on a peg cell) or a free
    /// bend point (<see cref="IsBendPoint"/> = true) that shapes the rope's visual path without
    /// requiring a peg at that cell.
    /// </summary>
    [Serializable]
    public struct RopeWaypoint
    {
        public Vector2Int PegCoord;
        public WindSide Side;
        public bool IsBendPoint;

        public RopeWaypoint(Vector2Int pegCoord, WindSide side = WindSide.None, bool isBendPoint = false)
        {
            PegCoord = pegCoord;
            Side = side;
            IsBendPoint = isBendPoint;
        }
    }

    /// <summary>
    /// A colored rope authored as an ordered path through peg cells. First/last waypoints are the
    /// endpoints (pin A / pin B); the ones in between are pegs the rope wraps around. <see cref="Tint"/>
    /// is a free color picked from a palette — at load time the material factory turns it into the
    /// rope's material and the materials of its two endpoint pins. <see cref="Layer"/> is the default
    /// over/under order at crossings (higher = on top); per-crossing exceptions live in
    /// <see cref="LevelDataSO.CrossingOverrides"/>.
    /// </summary>
    [Serializable]
    public class RopeData
    {
        public int RopeId;
        public Color Tint = Color.white;
        public int Layer;
        public List<RopeWaypoint> Path = new();

        public RopeData() { }

        public RopeData(int ropeId, Color tint, int layer)
        {
            RopeId = ropeId;
            Tint = tint;
            Layer = layer;
        }
    }
}
