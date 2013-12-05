using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Vydejna.Contracts
{
    public abstract class RestClient
    {
        private IHttpClient _client;
        private ParametrizedUrl _url;
        private List<RequestParameter> _parameters;
        private byte[] _payload;
        private string _method;

        protected RestClient(string url, IHttpClient client)
        {
            _client = client;
            _url = new ParametrizedUrl(url);
            _parameters = new List<RequestParameter>();
            _payload = new byte[0];
        }

        public RestClient AddParameter(string name, string value)
        {
            _parameters.Add(new RequestParameter(RequestParameterType.QueryString, name, value));
            return this;
        }
        public RestClient AddPath(string name, string value)
        {
            _parameters.Add(new RequestParameter(RequestParameterType.Path, name, value));
            return this;
        }
        public RestClient AddHeader(string name, string value)
        {
            _parameters.Add(new RequestParameter(RequestParameterType.Header, name, value));
            return this;
        }
        public RestClient UsingMethod(string method)
        {
            _method = method;
            return this;
        }

        protected void SetRawPayload(byte[] data)
        {
            _payload = data;
        }

        public async Task<RestClientResult> Execute()
        {
            var request = new HttpClientRequest();
            request.Url = _url.CompleteUrl(_parameters);
            request.Method = DetectMethod();
            request.Body = _payload;
            foreach (var header in _parameters)
            {
                if (header.Type == RequestParameterType.Header)
                    request.Headers.Add(new HttpClientHeader(header.Name, header.Value));
            }
            PreExecute(request);
            var response = await _client.Execute(request);
            return CreateResult(response);
        }

        public abstract RestClient SetPayload<T>(T data);
        protected abstract RestClientResult CreateResult(HttpClientResponse response);
        protected virtual void PreExecute(HttpClientRequest request) { }
        
        private string DetectMethod()
        {
            if (!string.IsNullOrEmpty(_method))
                return _method;
            if (_payload == null || _payload.Length == 0)
                return "GET";
            else
                return "POST";
        }
    }

    public abstract class RestClientResult
    {
        private int _statusCode;
        private byte[] _rawData;
        private RestClientHeaders _headers;

        protected RestClientResult(HttpClientResponse response)
        {
            _statusCode = response.StatusCode;
            _rawData = response.Body;
            _headers = new RestClientHeaders(response.Headers);
        }
        public int StatusCode { get { return _statusCode; } }
        public RestClientHeaders Headers { get { return _headers; } }
        protected byte[] RawData { get { return _rawData; } }
        public abstract T GetPayload<T>();
    }

    public class RestClientHeaders
    {
        private ILookup<string, HttpClientHeader> _headers;

        public RestClientHeaders(IEnumerable<HttpClientHeader> headers)
        {
            _headers = headers.ToLookup(h => h.Name);
        }

        public string this[string name]
        {
            get { return _headers[name].Select(h => h.Value).FirstOrDefault(); }
        }

        public string[] GetAll(string name)
        {
            return _headers[name].Select(h => h.Value).ToArray();
        }
    }

    public enum RequestParameterType
    {
        QueryString,
        PostData,
        Header,
        Path,
        Cookie
    }

    public class RequestParameter
    {
        public RequestParameterType Type { get; private set; }
        public string Name { get; private set; }
        public string Value { get; private set; }
        public RequestParameter(RequestParameterType type, string name, string value)
        {
            this.Type = type;
            this.Name = name;
            this.Value = value;
        }
        public override string ToString()
        {
            return string.Format("{0} {1}: {2}", Type, Name, Value);
        }
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            var oth = obj as RequestParameter;
            return oth != null 
                && Type == oth.Type 
                && string.Equals(Name, oth.Name, StringComparison.Ordinal) 
                && string.Equals(Value, oth.Value, StringComparison.Ordinal);
        }
    }


    public class RestClientJson : RestClient
    {
        public RestClientJson(string url, IHttpClient client)
            : base(url, client)
        {
        }

        public override RestClient SetPayload<T>(T data)
        {
            SetRawPayload(Encoding.UTF8.GetBytes(JsonSerializer.SerializeToString(data)));
            return this;
        }

        protected override RestClientResult CreateResult(HttpClientResponse response)
        {
            return new RestClientJsonResult(response);
        }
    }

    public class RestClientJsonResult : RestClientResult
    {
        public RestClientJsonResult(HttpClientResponse response)
            : base(response)
        {
        }

        public override T GetPayload<T>()
        {
            return JsonSerializer.DeserializeFromString<T>(Encoding.UTF8.GetString(RawData));
        }
    }

    public class RestClientXml : RestClient
    {
        public RestClientXml(string url, IHttpClient client)
            : base(url, client)
        {
        }

        public override RestClient SetPayload<T>(T data)
        {
            var serializer = new DataContractSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);
                SetRawPayload(stream.ToArray());
            }
            return this;
        }

        protected override RestClientResult CreateResult(HttpClientResponse response)
        {
            return new RestClientXmlResult(response);
        }
    }

    public class RestClientXmlResult : RestClientResult
    {
        public RestClientXmlResult(HttpClientResponse response)
            : base(response)
        {
        }

        public override T GetPayload<T>()
        {
            var serializer = new DataContractSerializer(typeof(T));
            using (var stream = new MemoryStream(RawData))
                return (T)serializer.ReadObject(stream);
        }
    }
}
