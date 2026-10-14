using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Level
{
    [Serializable]
    public class LevelJson
    {
        public int levelNumber;
        public string difficulty = "Easy";
        public int timeLimitSeconds;
        public List<StageJson> stages = new();
        public TutorialJson tutorial;

        public LevelDifficulty Difficulty => LevelDifficultyParser.Parse(difficulty);

        public bool HasTutorial => tutorial != null && tutorial.pinId >= 0 && stages.Count > 0
                                   && stages[0].pins.Exists(p => p.id == tutorial.pinId);

        // JsonUtility always instantiates nested [Serializable] fields; a missing "tutorial" keeps pinId = -1.
        public static LevelJson FromJson(string json) => JsonUtility.FromJson<LevelJson>(json);

        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    [Serializable]
    public class StageJson
    {
        public int mapId = 1;
        public int gridWidth = 4;
        public int gridHeight = 6;
        public List<PinJson> pins = new();
        public List<RopeJson> ropes = new();

        public static int WidthForMap(int mapId) => 3 + mapId;
        public static int HeightForMap(int mapId) => 5 + mapId;
    }

    [Serializable]
    public class PinJson
    {
        public int id;
        public int x;
        public int y;
        public bool locked;

        public Vector2Int Cell => new(x, y);
    }

    [Serializable]
    public class RopeJson
    {
        public int id;
        public int pinA;
        public int pinB;
        public int colorIndex;
        public int layer;
        public List<PathPointJson> path = new();
    }

    [Serializable]
    public struct PathPointJson
    {
        public float x;
        public float y;
        public float h;
    }

    [Serializable]
    public class TutorialJson
    {
        public int pinId = -1;
        public int toX;
        public int toY;

        public Vector2Int TargetCell => new(toX, toY);
    }

    public class LevelRepository
    {
        public const string Folder = "Levels/";

        public int Count { get; }

        public LevelRepository()
        {
            Count = Resources.LoadAll<TextAsset>(Folder).Length;
        }

        public const int FirstLoopLevel = 2;

        public static string FileName(int levelNumber) => $"level_{levelNumber:000}";

        public LevelJson Load(int levelNumber)
        {
            int n = FileNumber(levelNumber);
            var asset = Resources.Load<TextAsset>(Folder + FileName(n));
            if (asset == null)
                throw new InvalidOperationException($"Level json not found: {FileName(n)}");
            return LevelJson.FromJson(asset.text);
        }

        public LevelDifficulty DifficultyOf(int levelNumber)
        {
            var asset = Resources.Load<TextAsset>(Folder + FileName(FileNumber(levelNumber)));
            if (asset == null) return LevelDifficulty.Easy;
            return LevelDifficultyParser.Parse(JsonUtility.FromJson<DifficultyOnly>(asset.text).difficulty);
        }

        [Serializable]
        private class DifficultyOnly
        {
            public string difficulty;
        }

        // Level numbers keep counting up after the last file. Past the end I replay the levels from
        // FirstLoopLevel on in a shuffled order: every round is a fresh shuffle seeded by the round index
        // (same number -> same level, so a saved level number always reloads the same board), and a round
        // never starts with the level the previous round ended on.
        public int FileNumber(int levelNumber)
        {
            if (Count == 0) return 1;
            if (levelNumber <= Count) return Mathf.Max(1, levelNumber);

            int loopSize = Count - FirstLoopLevel + 1;
            if (loopSize <= 1) return Count;

            int index = levelNumber - Count - 1;
            int round = index / loopSize;
            var order = LoopOrder(round);
            if (round > 0)
            {
                var previous = LoopOrder(round - 1);
                if (order[0] == previous[loopSize - 1]) (order[0], order[1]) = (order[1], order[0]);
            }
            return order[index % loopSize];
        }

        private int[] LoopOrder(int round)
        {
            int size = Count - FirstLoopLevel + 1;
            var order = new int[size];
            for (int i = 0; i < size; i++) order[i] = FirstLoopLevel + i;

            var random = new System.Random(round * 7919 + 17);
            for (int i = size - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
    }
}
