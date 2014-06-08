using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace ServiceLib
{
    public static class JsonSerializer
    {
        private static JsonSerializerSettings _serializerSettings;

        static JsonSerializer()
        {
            _serializerSettings = new JsonSerializerSettings();
            _serializerSettings.Converters.Add(new IsoDateTimeConverter());
            _serializerSettings.Converters.Add(new StringEnumConverter());
        }

        public static string SerializeToString<T>(T value)
        {
            return JsonConvert.SerializeObject(value, _serializerSettings);
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
            return JsonConvert.SerializeObject(evt, _serializerSettings);
        }
    }
}
