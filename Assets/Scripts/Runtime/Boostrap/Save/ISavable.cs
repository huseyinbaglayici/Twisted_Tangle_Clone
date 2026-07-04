namespace Runtime.Boostrap.Save
{
    public interface ISavable
    {
    }

    public interface ISavable<T> : ISavable
    {
        T Load();
        void Save(T value);
    }
}