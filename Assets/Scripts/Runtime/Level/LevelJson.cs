using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Level
{
    // Single source of truth: runtime reads these files, the Level Creator edits the very same files.
    // Field names must match JSON exactly (JsonUtility). Unknown fields (e.g. "unsupportedFeatures") are ignored.
    [Serializable]
    public class LevelJson
    {
        public int levelNumber;
        public string difficulty = "Easy";
        public int moveLimit;          // 0 = unlimited
        public int timeLimitSeconds;   // 0 = unlimited
        public List<StageJson> stages = new();
        public TutorialJson tutorial;  // pinId < 0 when absent

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

        // Map N => (3+N) columns x (5+N) rows.
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
        public int layer; // render order only (higher = drawn on top). Does NOT affect win logic.
        // Authored rope shape (physics-settled, 13 points, cell space + height).
        // Optional: empty => straight rope. Needed for 1:1 tangles (slack ropes bulge sideways and overlap).
        public List<PathPointJson> path = new();
    }

    [Serializable]
    public struct PathPointJson
    {
        public float x; // cell-space column (float)
        public float y; // cell-space row (float)
        public float h; // height above board (pins ~0.2)
    }

    [Serializable]
    public class TutorialJson
    {
        public int pinId = -1;
        public int toX;
        public int toY;
        public bool lockOtherPins = true;

        public Vector2Int TargetCell => new(toX, toY);
    }

    public class LevelRepository
    {
        // Files live under Assets/Resources/Levels/level_001.json ... (TextAsset).
        public const string Folder = "Levels/";

        public int Count { get; }

        public LevelRepository()
        {
            Count = Resources.LoadAll<TextAsset>(Folder).Length;
        }

        public static string FileName(int levelNumber) => $"level_{levelNumber:000}";

        // After the last level, keep looping.
        public LevelJson Load(int levelNumber)
        {
            int n = Count == 0 ? 1 : ((levelNumber - 1) % Count) + 1;
            var asset = Resources.Load<TextAsset>(Folder + FileName(n));
            if (asset == null)
                throw new InvalidOperationException($"Level json not found: {FileName(n)}");
            return LevelJson.FromJson(asset.text);
        }
    }
}
