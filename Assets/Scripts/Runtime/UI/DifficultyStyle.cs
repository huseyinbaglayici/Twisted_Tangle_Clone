using System;
using Runtime.Level;
using UnityEngine;

namespace Runtime.UI
{
    public enum DifficultySlot
    {
        Header,
        Badge,
        Tab,
        Button,
        Bar,
        Ribbon,
        Backdrop,
        Pattern,
        Icon,
        Title
    }

    // Per-difficulty look of every tinted UI piece. Easy and Medium have no theme and keep the authored look.
    [CreateAssetMenu(fileName = "DifficultyStyle", menuName = "TwistedTangle/Difficulty Style", order = 3)]
    public class DifficultyStyle : ScriptableObject
    {
        [Serializable]
        public class Theme
        {
            public string name;
            public Sprite icon;
            public Sprite pattern;
            public Sprite label;
            public Color header = Color.white;
            public Color badge = Color.white;
            public Color tab = Color.white;
            public Color button = Color.white;
            public Color bar = Color.white;
            public Color ribbon = Color.white;
            public Color backdrop = Color.white;

            public string LevelTitle => name + " LEVEL";

            public Color ColorOf(DifficultySlot slot) => slot switch
            {
                DifficultySlot.Header => header,
                DifficultySlot.Badge => badge,
                DifficultySlot.Tab => tab,
                DifficultySlot.Button => button,
                DifficultySlot.Bar => bar,
                DifficultySlot.Ribbon => ribbon,
                DifficultySlot.Backdrop => backdrop,
                _ => Color.white
            };
        }

        [SerializeField] private Theme hard = new();
        [SerializeField] private Theme veryHard = new();

        public Theme For(LevelDifficulty difficulty) => difficulty switch
        {
            LevelDifficulty.Hard => hard,
            LevelDifficulty.VeryHard => veryHard,
            _ => null
        };
    }
}
