using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    /// <summary>
    /// The complete, serialized definition of one level: the single source of truth shared by the
    /// editor (authoring) and the runtime loader (playback). Everything authored in the tool must
    /// round-trip through these fields with no loss.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Level_0",
        menuName = "TwistedTangle/Level Data",
        order = 1)]
    public class LevelDataSO : ScriptableObject
    {
        [SerializeField] private int levelId = -1;
        [SerializeField] private int gridWidth = 5;
        [SerializeField] private int gridHeight = 5;
        [SerializeField] private int timeSeconds = 45;
        [SerializeField] private List<PegData> pegs = new();
        [SerializeField] private List<RopeData> ropes = new();
        [SerializeField] private List<CrossingOverride> crossingOverrides = new();

        public int LevelId
        {
            get => levelId;
            set => levelId = value;
        }

        public int GridWidth
        {
            get => gridWidth;
            set => gridWidth = value;
        }

        public int GridHeight
        {
            get => gridHeight;
            set => gridHeight = value;
        }

        /// <summary>How long the level lasts, in seconds.</summary>
        public int TimeSeconds
        {
            get => timeSeconds;
            set => timeSeconds = value;
        }

        public List<PegData> Pegs => pegs;
        public List<RopeData> Ropes => ropes;
        public List<CrossingOverride> CrossingOverrides => crossingOverrides;
    }
}
