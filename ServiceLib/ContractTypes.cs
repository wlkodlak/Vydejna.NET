using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ServiceLib
{
    public interface ITypeMapper
    {
        string GetName(Type type);
        Type GetType(string name);
        IEnumerable<Type> GetAllTypes();
    }

    public interface IRegisterTypes
    {
        void Register(TypeMapper mapper);
    }

    public class TypeMapper : ITypeMapper
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly Dictionary<string, Type> _byName = new Dictionary<string, Type>();
        private readonly Dictionary<Type, string> _byType = new Dictionary<Type, string>();

        public void Register(Type type, string name)
        {
            _lock.EnterWriteLock();
            try
            {
                _byName[name] = type;
                _byType[type] = name;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Register<T>()
        {
            Register(typeof (T), CreateTypeName(typeof (T)));
        }

        private static string CreateTypeName(Type type)
        {
            return type.FullName.Replace('+', '.');
        }

        public string GetName(Type type)
        {
            _lock.EnterReadLock();
            try
            {
                string name;
                _byType.TryGetValue(type, out name);
                return name;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public Type GetType(string name)
        {
            _lock.EnterReadLock();
            try
            {
                Type type;
                _byName.TryGetValue(name, out type);
                return type;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Register(IRegisterTypes registrator)
        {
            registrator.Register(this);
        }

        public IEnumerable<Type> GetAllTypes()
        {
            return _byType.Keys.ToList();
        }
    }
}