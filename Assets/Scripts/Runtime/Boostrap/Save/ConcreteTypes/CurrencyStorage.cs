using ES3KeyManager;

namespace Runtime.Boostrap.Save.ConcreteTypes
{
    public class CurrencyStorage : ISavable<int>
    {
        private const string Currency = "Currency";

        public int Load()
        {
            if (ES3.KeyExists(Currency))
                return ES3Keys.LoadInt(Currency);
            return -999;
        }

        public void Save(int value)
        {
            if (!ES3.KeyExists(Currency))
                return;
            ES3Keys.SaveInt(Currency, value);
        }
    }
}