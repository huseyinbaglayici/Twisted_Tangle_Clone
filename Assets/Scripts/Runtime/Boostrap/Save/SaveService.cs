using ES3KeyManager;

namespace Runtime.Boostrap.Save
{
    // Player progress on disk. Keys and defaults live in Resources/ES3KeyRegistry.
    public class SaveService
    {
        private const string LevelKey = "Level";

        public int Level
        {
            get => ES3Keys.LoadInt(LevelKey);
            set => ES3Keys.SaveInt(LevelKey, value);
        }
    }
}
