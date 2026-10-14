namespace Runtime.Level
{
    public enum LevelDifficulty
    {
        Easy,
        Medium,
        Hard,
        VeryHard
    }

    public static class LevelDifficultyParser
    {
        public static LevelDifficulty Parse(string value)
        {
            string key = (value ?? string.Empty).Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            return key switch
            {
                "medium" => LevelDifficulty.Medium,
                "hard" => LevelDifficulty.Hard,
                "veryhard" or "superhard" => LevelDifficulty.VeryHard,
                _ => LevelDifficulty.Easy
            };
        }

        public static bool IsHard(this LevelDifficulty difficulty) => difficulty >= LevelDifficulty.Hard;
    }
}
