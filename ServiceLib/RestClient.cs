using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public abstract class RestClient
    {
        private readonly IHttpClient _client;
        private readonly ParametrizedUrl _url;
        private readonly List<RequestParameter> _parameters;
        private byte[] _payload;
        private string _method;
        private HttpClientRequest _currentRequest;
        private readonly Stopwatch _stopwatch;
        private readonly IRestClientLog _externalLog;

        protected RestClient(string url, IHttpClient client, IRestClientLog log)
        {
            _client = client;
            _url = new ParametrizedUrl(url);
            _parameters = new List<RequestParameter>();
            _payload = new byte[0];
            _stopwatch = new Stopwatch();
            _externalLog = log ?? new InternalLog("ServiceLib.RestClient");
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

        public Task<RestClientResult> Execute()
        {
            _currentRequest = new HttpClientRequest();
            _currentRequest.Url = _url.CompleteUrl(_parameters);
            _currentRequest.Method = DetectMethod();
            _currentRequest.Body = _payload;
            foreach (var header in _parameters)
            {
                if (header.Type == RequestParameterType.Header)
                    _currentRequest.Headers.Add(new HttpClientHeader(header.Name, header.Value));
            }
            PreExecute(_currentRequest);
            _stopwatch.Reset();
            _stopwatch.Start();
            return _client.Execute(_currentRequest).ContinueWith<RestClientResult>(ProcessHttpResponse);
        }

        private RestClientResult ProcessHttpResponse(Task<HttpClientResponse> responseTask)
        {
            _stopwatch.Stop();
            if (responseTask.IsCanceled)
                throw new TaskCanceledException(responseTask);
            if (responseTask.Exception != null)
            {
                _externalLog.LogRestCall(
                    _currentRequest, null, responseTask.Exception.InnerException, _stopwatch.ElapsedMilliseconds);
                throw responseTask.Exception;
            }
            else
            {
                var response = responseTask.Result;
                _externalLog.LogRestCall(_currentRequest, response, null, _stopwatch.ElapsedMilliseconds);
                return CreateResult(response);
            }
        }

        public abstract RestClient SetPayload<T>(T data);
        protected abstract RestClientResult CreateResult(HttpClientResponse response);

        protected virtual void PreExecute(HttpClientRequest request)
        {
        }

        private string DetectMethod()
        {
            if (!string.IsNullOrEmpty(_method))
                return _method;
            if (_payload == null || _payload.Length == 0)
                return "GET";
            else
                return "POST";
        }

        private class InternalLog : TraceSource, IRestClientLog
        {
            public InternalLog(string name)
                : base(name)
            {
            }

            public void LogRestCall(
                HttpClientRequest request, HttpClientResponse response, Exception error, long milliseconds)
            {
                var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Called service {Url}");
                msg.SetProperty("Url", false, request.Url);
                msg.SetProperty("Method", false, request.Method);
                msg.SetProperty("RequestHeaders", true, BuildHeaders(request.Headers));
                msg.SetProperty("RequestBody", true, request.Body);
                if (response != null)
                {
                    msg.SetProperty("StatusCode", false, response.StatusCode);
                    msg.SetProperty("ResponseHeaders", true, BuildHeaders(response.Headers));
                    msg.SetProperty("ResponseBody", true, response.Body);
                }
                if (error != null)
                {
                    msg.SetProperty("Exception", true, error);
                }
                msg.SetProperty("Duration", false, milliseconds);
                msg.Log(this);
            }

            private static StringBuilder BuildHeaders(List<HttpClientHeader> headers)
            {
                var stringBuilder = new StringBuilder();
                for (var index = 0; index < headers.Count; index++)
                {
                    var header = headers[index];
                    if (index != 0)
                        stringBuilder.AppendLine();
                    stringBuilder.Append(header.Name).Append(": ").Append(header.Value);
                }
                return stringBuilder;
            }
        }
    }

    public abstract class RestClientResult
    {
        private readonly int _statusCode;
        private readonly byte[] _rawData;
        private readonly RestClientHeaders _headers;

        protected RestClientResult(HttpClientResponse response)
        {
            _statusCode = response.StatusCode;
            _rawData = response.Body;
            _headers = new RestClientHeaders(response.Headers);
        }

        public int StatusCode
        {
            get { return _statusCode; }
        }

        public RestClientHeaders Headers
        {
            get { return _headers; }
        }

        protected byte[] RawData
        {
            get { return _rawData; }
        }

        public abstract T GetPayload<T>();
    }

    public class RestClientHeaders
    {
        private readonly ILookup<string, HttpClientHeader> _headers;

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
            Type = type;
            Name = name;
            Value = value;
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

    public interface IRestClientLog
    {
        void LogRestCall(HttpClientRequest request, HttpClientResponse response, Exception error, long milliseconds);
    }

    public class RestClientJson : RestClient
    {
        public RestClientJson(string url, IHttpClient client, IRestClientLog log = null)
            : base(url, client, log)
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
            var stringData = Encoding.UTF8.GetString(RawData);
            if (typeof (T) == typeof (string))
                return (T) (object) stringData;
            return JsonSerializer.DeserializeFromString<T>(stringData);
        }
    }

    public class RestClientXml : RestClient
    {
        public RestClientXml(string url, IHttpClient client, IRestClientLog log = null)
            : base(url, client, log)
        {
        }

        public override RestClient SetPayload<T>(T data)
        {
            var serializer = new DataContractSerializer(typeof (T));
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
            var serializer = new DataContractSerializer(typeof (T));
            using (var stream = new MemoryStream(RawData))
                return (T) serializer.ReadObject(stream);
        }
    }
}