namespace Runtime.Boostrap.Save
{
    public interface ISavable<T>
    {
        T Load();
        void Save(T value);
    }
}