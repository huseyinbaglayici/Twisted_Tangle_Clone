using ES3KeyManager;

namespace Runtime.Boostrap.Save.ConcreteTypes
{
    public class LevelIdProgression : ISavable<int>
    {
        private const string LevelID = "LevelID";

        public int Load()
        {
            if (ES3.KeyExists(LevelID))
                return ES3Keys.LoadInt(LevelID);
            return 0;
        }

        public void Save(int value)
        {
            if (!ES3.KeyExists(LevelID))
                return;
            ES3Keys.SaveInt(LevelID, value);
        }
    }
}