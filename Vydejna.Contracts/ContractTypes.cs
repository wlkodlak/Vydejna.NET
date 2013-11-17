using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class ContractTypes
    {
        private static volatile bool _initialized;
        private static Dictionary<string, Type> _byName;
        private static Dictionary<Type, string> _byType;

        private static void Setup()
        {
            if (_initialized)
                return;
            lock (typeof(ContractTypes))
            {
                if (_initialized)
                    return;
                _initialized = true;

                var allTypes = typeof(ContractTypes).Assembly.GetTypes();

                _byName = new Dictionary<string, Type>();
                _byType = new Dictionary<Type, string>();
                foreach (var type in allTypes)
                {
                    if (!TypeFilter(type))
                        continue;
                    var name = GetTypeName(type);
                    _byName[name] = type;
                    _byType[type] = name;
                }
            }
        }

        private static bool TypeFilter(Type type)
        {
            return true;
        }

        private static string GetTypeName(Type type)
        {
            return type.Name;
        }

        public static Type GetType(string typeName)
        {
            Setup();
            Type type;
            if (_byName.TryGetValue(typeName, out type))
                return type;
            else
                return null;
        }

        public static string GetType(Type type)
        {
            Setup();
            string name;
            if (_byType.TryGetValue(type, out name))
                return name;
            else
                return null;
        }
    }
}
