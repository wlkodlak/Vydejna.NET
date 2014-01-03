using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestStagedContext : IHttpServerStagedContext
    {
        private string _method, _url, _client;
        private string _input;
        private IHttpSerializer _serializer;
        private IHttpServerStagedHeaders _inputHeaders, _outputHeaders;
        private Dictionary<string, List<string>> _parameters;
        private List<RequestParameter> _allParameters;
        private ManualResetEventSlim _mre;

        public TestStagedContext(string method, string url, string client)
        {
            _method = method;
            _url = url;
            _client = client;
            _input = "";
            _inputHeaders = new HttpServerStagedContextHeaders();
            _outputHeaders = new HttpServerStagedContextHeaders();
            _parameters = new Dictionary<string, List<string>>();
            _allParameters = new List<RequestParameter>();
            _mre = new ManualResetEventSlim();

            foreach (var param in ParametrizedUrl.ParseQueryString(_url))
            {
                _allParameters.Add(param);
                var key = GetKey(param.Type, param.Name);
                List<string> values;
                if (!_parameters.TryGetValue(key, out values))
                    _parameters[key] = values = new List<string>();
                values.Add(param.Value);
            }
        }

        public TestStagedContext WithInputString(string input)
        {
            _input = input;
            return this;
        }
        
        public TestStagedContext WithSerializer(IHttpSerializer serializer)
        {
            _serializer = serializer;
            return this;
        }

        public TestStagedContext WithParameter(RequestParameterType type, string name, string value)
        {
            var key = GetKey(type, name);
            List<string> values;
            if (!_parameters.TryGetValue(key, out values))
                _parameters[key] = values = new List<string>();
            values.Add(value);
            _allParameters.Add(new RequestParameter(type, name, value));
            return this;
        }

        public TestStagedContext WithHeader(string name, string value)
        {
            _inputHeaders.Add(name, value);
            return this;
        }

        private static string GetKey(RequestParameterType type, string name)
        {
            switch (type)
            {
                case RequestParameterType.Path:
                    return "R" + name;
                case RequestParameterType.PostData:
                    return "P" + name;
                case RequestParameterType.QueryString:
                    return "Q" + name;
                default:
                    return "!" + name;
            }
        }

        private IList<string> GetValues(string key)
        {
            List<string> values;
            if (_parameters.TryGetValue(key, out values))
                return values;
            else
                return null;
        }

        public static TestStagedContext Get(string pathAndQuery)
        {
            return new TestStagedContext("GET", "http://localhost" + pathAndQuery, "10.24.83.102");
        }

        public string Method { get { return _method; } }
        public string Url { get { return _url; } }
        public string ClientAddress { get { return _client; } }
        public string InputString { get { return _input; } }
        public int StatusCode { get; set; }
        public string OutputString { get; set; }

        public IHttpSerializer InputSerializer { get { return _serializer; } }
        public IHttpSerializer OutputSerializer { get { return _serializer; } }
        public IHttpServerStagedHeaders InputHeaders { get { return _inputHeaders; } }
        public IHttpServerStagedHeaders OutputHeaders { get { return _outputHeaders; } }
        public IEnumerable<RequestParameter> RawParameters { get { return _allParameters; } }
        public IHttpProcessedParameter Parameter(string name)
        {
            return new HttpProcessedParameter(RequestParameterType.QueryString, name, GetValues(GetKey(RequestParameterType.QueryString, name)));
        }
        public IHttpProcessedParameter PostData(string name)
        {
            return new HttpProcessedParameter(RequestParameterType.QueryString, name, GetValues(GetKey(RequestParameterType.PostData, name)));
        }
        public IHttpProcessedParameter Route(string name)
        {
            return new HttpProcessedParameter(RequestParameterType.QueryString, name, GetValues(GetKey(RequestParameterType.Path, name)));
        }

        public void Close()
        {
            _mre.Set();
        }

        public bool WaitForClose(int timeout = 100)
        {
            return _mre.Wait(timeout);
        }
    }
}
