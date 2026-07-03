namespace Runtime.Boostrap.Save.ConcreteTypes
{
    public class LevelIdProgression : ISavable<int>
    {
        public int Load()
        {
            return 1;
        }

        public void Save(int value)
        {
            throw new System.NotImplementedException();
        }
    }
}