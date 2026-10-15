using ES3KeyManager;

namespace Runtime.Boostrap.Save
{
    public class SaveService
    {
        private const string LevelKey = "Level";
        private const string SoundOnKey = "SoundOn";
        private const string HapticsOnKey = "HapticsOn";
        private const string TutorialShownSuffix = "TutorialShown";

        public int Level
        {
            get => ES3Keys.LoadInt(LevelKey);
            set => ES3Keys.SaveInt(LevelKey, value);
        }

        public bool SoundOn
        {
            get => ES3Keys.LoadBool(SoundOnKey);
            set => ES3Keys.SaveBool(SoundOnKey, value);
        }

        public bool HapticsOn
        {
            get => ES3Keys.LoadBool(HapticsOnKey);
            set => ES3Keys.SaveBool(HapticsOnKey, value);
        }

        // One flag per power-up: its explaining popup shows only the first time it is used.
        public bool IsTutorialShown(string powerUp) => ES3Keys.LoadBool(powerUp + TutorialShownSuffix);

        public void MarkTutorialShown(string powerUp) => ES3Keys.SaveBool(powerUp + TutorialShownSuffix, true);
    }
}
