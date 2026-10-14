using Runtime.Level;
using UnityEngine;

namespace Runtime.UI
{
    public static class DifficultyStyle
    {
        public static readonly Color Hard = new(0.58f, 0.3f, 0.92f);
        public static readonly Color VeryHard = new(0.9f, 0.2f, 0.24f);

        public static Color ColorOf(LevelDifficulty difficulty, Color normal) => difficulty switch
        {
            LevelDifficulty.Hard => Hard,
            LevelDifficulty.VeryHard => VeryHard,
            _ => normal
        };

        public static string NameOf(LevelDifficulty difficulty) =>
            difficulty == LevelDifficulty.VeryHard ? "SUPER HARD" : "HARD";
    }
}
