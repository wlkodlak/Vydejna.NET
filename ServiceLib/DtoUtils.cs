using System.Text;

namespace ServiceLib
{
    public static class DtoUtils
    {
        public static int GetHashCode<T>(T obj)
        {
            unchecked
            {
                var type = typeof (T);
                int hash = 48972847;
                foreach (var property in type.GetProperties())
                {
                    var value = property.GetValue(obj, null);
                    hash *= 30481;
                    if (value != null)
                        hash += value.GetHashCode();
                }
                return hash;
            }
        }

        public static bool Equals<T>(T a, object b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);
            else if (ReferenceEquals(b, null))
                return false;
            else if (a.GetType() != typeof (T) || b.GetType() != typeof (T))
                return false;
            else
            {
                foreach (var property in typeof (T).GetProperties())
                {
                    var valA = property.GetValue(a, null);
                    var valB = property.GetValue(b, null);
                    if (!object.Equals(valA, valB))
                        return false;
                }
                return true;
            }
        }

        public static string ToString<T>(T obj)
        {
            if (ReferenceEquals(obj, null))
                return "null";
            var sb = new StringBuilder();
            sb.Append(typeof (T).Name).Append(" { ");
            bool first = true;
            foreach (var property in typeof (T).GetProperties())
            {
                var value = property.GetValue(obj, null);
                if (first)
                    first = false;
                else
                    sb.Append(", ");
                sb.Append(property.Name).Append(" = ").Append(value);
            }
            sb.Append(first ? "}" : " }");
            return sb.ToString();
        }
    }
}