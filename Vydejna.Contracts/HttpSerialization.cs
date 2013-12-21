using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceStack.Text;
using System.Xml;
using System.Runtime.Serialization;
using System.IO;

namespace Vydejna.Contracts
{
    public interface IHttpSerializer
    {
        string ContentType { get; }
        bool ConsumesContentType(string contentType);
        bool ProducesContentType(string acceptType);
        T Deserialize<T>(string data);
        string Serialize<T>(T data);
    }

    public class HttpSerializerJson : IHttpSerializer
    {
        public string ContentType
        {
            get { return "application/json"; }
        }

        public bool ConsumesContentType(string contentType)
        {
            switch (contentType)
            {
                case "application/json":
                case "text/json":
                    return true;
                default:
                    return false;
            }
        }

        public bool ProducesContentType(string acceptType)
        {
            switch (acceptType)
            {
                case "application/json":
                case "text/json":
                case "application/*":
                case "*/json":
                case "*/*":
                    return true;
                default:
                    return false;
            }
        }

        public T Deserialize<T>(string data)
        {
            return JsonSerializer.DeserializeFromString<T>(data);
        }

        public string Serialize<T>(T data)
        {
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class HttpSerializerXml : IHttpSerializer
    {
        public string ContentType
        {
            get { return "text/xml"; }
        }

        public bool ConsumesContentType(string contentType)
        {
            switch (contentType)
            {
                case "application/xml":
                case "text/xml":
                    return true;
                default:
                    return false;
            }
        }

        public bool ProducesContentType(string acceptType)
        {
            switch (acceptType)
            {
                case "application/xml":
                case "text/xml":
                case "application/*":
                case "*/xml":
                case "*/*":
                    return true;
                default:
                    return false;
            }
        }

        public T Deserialize<T>(string data)
        {
            var serializer = new DataContractSerializer(typeof(T));
            return (T)serializer.ReadObject(XmlDictionaryReader.Create(new StringReader(data)));
        }

        public string Serialize<T>(T data)
        {
            var serializer = new DataContractSerializer(typeof(T));
            using (var stringWriter = new StringWriter())
            {
                using (var xmlWriter = XmlDictionaryWriter.Create(stringWriter))
                    serializer.WriteObject(xmlWriter, data);
                return stringWriter.ToString();
            }
        }
    }

    public class HttpSerializerPicker : ISerializerPicker
    {
        public IHttpSerializer PickSerializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next)
        {
            var serializer = PickSerializerCore(context, options);
            if (serializer != null)
                return serializer;
            else if (next != null)
                return next.PickSerializer(context, options, null);
            else
                return null;
        }

        private static IHttpSerializer PickSerializerCore(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options)
        {
            var serializer = (IHttpSerializer)null;
            var acceptTypes = context.InputHeaders.AcceptTypes;
            if (acceptTypes == null)
                serializer = options.FirstOrDefault();
            else
                serializer = acceptTypes
                    .Select(t => options.FirstOrDefault(s => s.ProducesContentType(t)))
                    .FirstOrDefault(s => s != null);
            return serializer;
        }

        public IHttpSerializer PickDeserializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next)
        {
            var deserializer = (IHttpSerializer)null;
            var contentType = context.InputHeaders.ContentType;
            if (deserializer == null && contentType != null)
                deserializer = options.FirstOrDefault(s => s.ConsumesContentType(contentType));
            if (deserializer == null)
                deserializer = PickSerializerCore(context, options);
            if (deserializer == null && next != null)
                deserializer = next.PickDeserializer(context, options, null);
            return deserializer;
        }
    }

    public class HttpSerializerPickerProxy : ISerializerPicker
    {
        private ISerializerPicker _primary, _secondary;

        public HttpSerializerPickerProxy(ISerializerPicker primary, ISerializerPicker secondary)
        {
            _primary = primary;
            _secondary = secondary;
        }

        public IHttpSerializer PickDeserializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next)
        {
            return _primary.PickDeserializer(context, options, _secondary);
        }

        public IHttpSerializer PickSerializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next)
        {
            return _primary.PickSerializer(context, options, _secondary);
        }
    }
}
