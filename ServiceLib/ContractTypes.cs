using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface ITypeMapper
    {
        string GetName(Type type);
        Type GetType(string name);
    }

    public interface IRegisterTypes
    {
        void Register(TypeMapper mapper);
    }

    public class TypeMapper : ITypeMapper
    {
        private UpdateLock _lock = new UpdateLock();
        private Dictionary<string, Type> _byName = new Dictionary<string, Type>();
        private Dictionary<Type, string> _byType = new Dictionary<Type, string>();

        public void Register(Type type, string name)
        {
            using (_lock.Update())
            {
                _lock.Write();
                _byName[name] = type;
                _byType[type] = name;
            }
        }

        public void Register<T>()
        {
            Register(typeof(T), CreateTypeName(typeof(T)));
        }

        private static string CreateTypeName(Type type)
        {
            return type.FullName.Replace('+', '.');
        }

        public string GetName(Type type)
        {
            using (_lock.Read())
            {
                string name;
                _byType.TryGetValue(type, out name);
                return name;
            }
        }

        public Type GetType(string name)
        {
            using (_lock.Read())
            {
                Type type;
                _byName.TryGetValue(name, out type);
                return type;
            }
        }

        public void Register(IRegisterTypes registrator)
        {
            registrator.Register(this);
        }
    }
}
