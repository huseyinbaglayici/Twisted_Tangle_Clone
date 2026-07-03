using ES3KeyManager;

namespace Runtime.Boostrap.Save.ConcreteTypes
{
    public class HealthStorage : ISavable<byte>
    {
        private const string Health = "Health";

        public byte Load()
        {
            if (ES3.KeyExists(Health))
                return ES3Keys.LoadByte(Health);
            return 0;
        }

        public void Save(byte value)
        {
            if (!ES3.KeyExists(Health))
                return;
            ES3Keys.SaveByte(Health, value);
        }
    }
}