using System;
using System.Collections.Generic;

namespace Runtime.Boostrap.Save
{
    public class SaveService
    {
        private readonly Dictionary<Type, object> _storages = new();

        public void Register<TStorage>(TStorage storage) where TStorage : ISavable
        {
            _storages[typeof(TStorage)] = storage;
        }

        public TStorage Get<TStorage>()
        {
            return (TStorage)_storages[typeof(TStorage)];
        }
    }
}