using System.Collections.Generic;
using TwistedTangle.Runtime.Data.Enums;
using TwistedTangle.Runtime.Data.ValueObjects;
using UnityEngine;

namespace TwistedTangle.Runtime.Data.ScriptableObjects
{
    [CreateAssetMenu(
        fileName = "Level_0",
        menuName = "TwistedTangle/Level Data",
        order = 1)]
    public class LevelDataSO : ScriptableObject
    {
        [SerializeField] private int levelId = -1;
        [SerializeField] private LevelDifficulty difficulty = LevelDifficulty.Normal;
        [SerializeField] private int gridWidth = 5;
        [SerializeField] private int gridHeight = 5;
        [SerializeField] private int timeSeconds = 45;
        [SerializeField] private List<GridEntityData> gridEntities = new();
        [SerializeField] private List<RopeData> ropes = new();
        [SerializeField] private List<CrossingOverride> crossingOverrides = new();
        [SerializeField] private Material backgroundMaterial;

        public int LevelId
        {
            get => levelId;
            set => levelId = value;
        }

        public LevelDifficulty Difficulty
        {
            get => difficulty;
            set => difficulty = value;
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

        public int TimeSeconds
        {
            get => timeSeconds;
            set => timeSeconds = value;
        }

        public List<GridEntityData> GridEntities => gridEntities;
        public List<RopeData> Ropes => ropes;
        public List<CrossingOverride> CrossingOverrides => crossingOverrides;
        public Material BackgroundMaterial { get => backgroundMaterial; set => backgroundMaterial = value; }
    }
}
