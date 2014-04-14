using Newtonsoft.Json;
using System;

namespace ServiceLib
{
    public static class JsonSerializer
    {
        public static string SerializeToString<T>(T value)
        {
            return JsonConvert.SerializeObject(value, typeof(T), new JsonSerializerSettings());
        }

        public static T DeserializeFromString<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static object DeserializeFromString(string json, Type type)
        {
            return JsonConvert.DeserializeObject(json, type);
        }

        public static string SerializeToString(object evt, Type type)
        {
            return JsonConvert.SerializeObject(evt, type, new JsonSerializerSettings());
        }
    }
}
