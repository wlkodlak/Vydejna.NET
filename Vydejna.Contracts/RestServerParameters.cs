using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpServerRequest
    {
        public HttpServerRequest()
        {
            Headers = new HttpServerHeaders();
            _parameters = new Dictionary<string, List<RequestParameter>>();
        }
        public string Method { get; set; }
        public string Url { get; set; }
        public HttpServerHeaders Headers;

        private Dictionary<string, List<RequestParameter>> _parameters;
        public void AddParameter(RequestParameter parameter)
        {
            var list = GetParameters(parameter.Type, parameter.Name);
            list.Add(parameter);
        }
        private IList<RequestParameter> GetParameters(RequestParameterType type, string name)
        {
            string key = TypePrefix(type) + name;
            List<RequestParameter> list;
            if (!_parameters.TryGetValue(key, out list))
                _parameters[key] = list = new List<RequestParameter>();
            return list;
        }
        private string TypePrefix(RequestParameterType type)
        {
            switch (type)
            {
                case RequestParameterType.QueryString:
                    return "G";
                case RequestParameterType.Path:
                    return "X";
                default:
                    return "?";
            }
        }

        public HttpServerRequestParameter Parameter(string name)
        {
            var value = GetParameters(RequestParameterType.QueryString, name).Select(p => p.Value).FirstOrDefault();
            return new HttpServerRequestParameter(name, value);
        }

        public HttpServerRequestRoute Route(string name)
        {
            var value = GetParameters(RequestParameterType.Path, name).Select(p => p.Value).FirstOrDefault();
            return new HttpServerRequestRoute(name, value);
        }

    }

    public class HttpServerRequestParameter
    {
        private string _name;
        private string _value;

        public HttpServerRequestParameter(string name, string value)
        {
            this._name = name;
            this._value = value;
        }

        public HttpServerRequestParameter<int> AsInteger()
        {
            int parsedValue;
            if (string.IsNullOrEmpty(_value))
                return new HttpServerRequestParameter<int>(_name, false, 0);
            else if (int.TryParse(_value, out parsedValue))
                return new HttpServerRequestParameter<int>(_name, true, parsedValue);
            else
                throw new RequestParameterException(string.Format("Parameter {0} is not valid integer: {1}", _name, _value));
        }
        public HttpServerRequestParameter<string> AsString()
        {
            return new HttpServerRequestParameter<string>(_name, !string.IsNullOrEmpty(_value), _value);
        }
        HttpServerRequestParameter<T> As<T>(Func<string, T> convert)
        {
            if (string.IsNullOrEmpty(_value))
                return new HttpServerRequestParameter<T>(_name, false, default(T));
            try
            {
                var parsed = convert(_value);
                return new HttpServerRequestParameter<T>(_name, true, parsed);
            }
            catch (RequestParameterException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RequestParameterException(string.Format("Parameter {0} is not valid {1}: {2}", _name, typeof(T).Name, _value), ex);
            }
        }
    }

    public class HttpServerRequestRoute
    {
        private string _name;
        private string _value;

        public HttpServerRequestRoute(string name, string value)
        {
            this._name = name;
            this._value = value;
        }
        public int AsInteger()
        {
            int parsedValue;
            if (string.IsNullOrEmpty(_value))
                throw new RequestParameterException(string.Format("Parameter {0} is required", _name));
            else if (int.TryParse(_value, out parsedValue))
                return parsedValue;
            else
                throw new RequestParameterException(string.Format("Parameter {0} is not valid integer: {1}", _name, _value));
        }
        public string AsString()
        {
            return _value;
        }
        public T As<T>(Func<string, T> convert)
        {
            if (string.IsNullOrEmpty(_value))
                throw new RequestParameterException(string.Format("Parameter {0} is required", _name));
            try
            {
                return convert(_value);
            }
            catch (RequestParameterException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RequestParameterException(string.Format("Parameter {0} is not valid {1}: {2}", _name, typeof(T).Name, _value), ex);
            }
        }
    }

    public class HttpServerRequestParameter<T>
    {
        private string _name;
        private bool _present;
        private T _value;

        public HttpServerRequestParameter(string name, bool present, T value)
        {
            _name = name;
            _present = present;
            _value = value;
        }

        public T Mandatory()
        {
            if (_present)
                return _value;
            else
                throw new RequestParameterException(string.Format("Parameter {0} is missing", _name));
        }

        public T Optional(T defaultValue = default(T))
        {
            if (_present)
                return _value;
            else
                return defaultValue;
        }
    }

    [Serializable]
    public class RequestParameterException : Exception
    {
        public RequestParameterException() { }
        public RequestParameterException(string message) : base(message) { }
        public RequestParameterException(string message, Exception inner) : base(message, inner) { }
        protected RequestParameterException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }
    }

    public class HttpServerResponse
    {
        public HttpServerResponse()
        {
            Headers = new HttpServerHeaders();
        }
        public int StatusCode { get; set; }
        public HttpServerHeaders Headers { get; private set; }
        public byte[] RawBody { get; set; }
        public object ObjectBody { get; set; }
    }

    public class HttpServerResponseBuilder
    {
        private HttpServerResponse _response;

        public HttpServerResponseBuilder()
        {
            _response = new HttpServerResponse();
            _response.StatusCode = 200;
            _response.Headers.ContentType = "text/plain";
        }

        public HttpServerResponseBuilder Redirect(string url)
        {
            _response.StatusCode = 302;
            _response.Headers.Location = url;
            return this;
        }

        public HttpServerResponseBuilder WithStatusCode(int status)
        {
            _response.StatusCode = status;
            return this;
        }

        public HttpServerResponseBuilder WithStatusCode(System.Net.HttpStatusCode status)
        {
            _response.StatusCode = (int)status;
            return this;
        }

        public HttpServerResponseBuilder WithHeader(string name, string value)
        {
            _response.Headers.Add(name, value);
            return this;
        }

        public HttpServerResponseBuilder WithHeader(Action<HttpServerHeaders> action)
        {
            action(_response.Headers);
            return this;
        }

        public HttpServerResponseBuilder WithRawBody(byte[] body)
        {
            _response.RawBody = body;
            return this;
        }

        public HttpServerResponseBuilder WithBody(object body)
        {
            _response.ObjectBody = body;
            return this;
        }

        public  HttpServerResponse Build()
        {
            return _response;
        }
    }

    public class HttpServerHeader
    {
        private string _name, _value;
        public HttpServerHeader(string name, string value)
        {
            _name = name;
            _value = value;
        }
        public string Name { get { return _name; } }
        public string Value { get { return _value; } }
    }

    public class HttpServerHeaders : IEnumerable<HttpServerHeader>
    {
        private Dictionary<string, List<string>> _data;

        private string _acceptTypesRaw;
        private string _acceptLanguagesRaw;
        private IList<string> _acceptTypes;
        private IList<string> _acceptLanguages;
        private int _contentLength = -1;
        private string _contentType;
        private string _contentLanguage;
        private string _referer;
        private string _location;

        public HttpServerHeaders()
        {
            _data = new Dictionary<string, List<string>>();
        }

        public void Add(string name, string value)
        {
            switch (name)
            {
                case "Content-Type":
                    _contentType = value;
                    break;
                case "Content-Language":
                    _contentLanguage = value;
                    break;
                case "Content-Length":
                    _contentLength = string.IsNullOrEmpty(value) ? -1 : int.Parse(value);
                    break;
                case "Referer":
                    _referer = value;
                    break;
                case "Location":
                    _referer = value;
                    break;
                case "Accept":
                    _acceptTypesRaw = value;
                    if (string.IsNullOrEmpty(value))
                        _acceptTypes = null;
                    else
                    {
                        _acceptTypes = new List<string>();
                        var regex = new Regex(@"^ *([a-zA-Z0-9\-*]+[a-zA-Z0-9\-*])");
                        foreach (var acceptPart in value.Split(','))
                        {
                            var match = regex.Match(acceptPart);
                            if (match.Success)
                                _acceptTypes.Add(match.Groups[1].Value);
                        }
                    }
                    break;
                case "Accept-Language":
                    _acceptLanguagesRaw = value;
                    if (string.IsNullOrEmpty(value))
                        _acceptLanguages = null;
                    else
                    {
                        _acceptLanguages = new List<string>();
                        var regex = new Regex(@"^ *([a-zA-Z0-9\-*]+)");
                        foreach (var acceptPart in value.Split(','))
                        {
                            var match = regex.Match(acceptPart);
                            if (match.Success)
                                _acceptLanguages.Add(match.Groups[1].Value);
                        }
                    }
                    break;
                default:
                    List<string> list;
                    if (!_data.TryGetValue(name, out list))
                        _data[name] = list = new List<string>();
                    list.Add(value);
                    break;
            }
        }

        public string ContentType
        {
            get { return _contentType; }
            set { _contentType = value; }
        }
        public string ContentLanguage
        {
            get { return _contentLanguage; }
            set { _contentLanguage = value; }
        }
        public int ContentLength
        {
            get { return _contentLength; }
            set { _contentLength = value; }
        }
        public IList<string> AcceptTypes
        {
            get { return _acceptTypes; }
            set { _acceptTypes = value; }
        }
        public IList<string> AcceptLanguages
        {
            get { return _acceptLanguages; }
            set { _acceptLanguages = value; }
        }
        public string Referer
        {
            get { return _referer; }
            set { _referer = value; }
        }
        public string Location
        {
            get { return _location; }
            set { _location = value; }
        }

        public IEnumerator<HttpServerHeader> GetEnumerator()
        {
            if (!string.IsNullOrEmpty(_contentType))
                yield return new HttpServerHeader("Content-Type", _contentType);
            if (!string.IsNullOrEmpty(_acceptLanguagesRaw))
                yield return new HttpServerHeader("Content-Type", _acceptLanguagesRaw);
            if (!string.IsNullOrEmpty(_acceptTypesRaw))
                yield return new HttpServerHeader("Content-Type", _acceptTypesRaw);
            if (!string.IsNullOrEmpty(_contentLanguage))
                yield return new HttpServerHeader("Content-Type", _contentLanguage);
            if (_contentLength >= 0)
                yield return new HttpServerHeader("Content-Length", _contentLength.ToString());
            if (!string.IsNullOrEmpty(_referer))
                yield return new HttpServerHeader("Referer", _referer);
            if (!string.IsNullOrEmpty(_location))
                yield return new HttpServerHeader("Location", _location);
            foreach (var headerPair in _data)
            {
                foreach (var headerValue in headerPair.Value)
                    yield return new HttpServerHeader(headerPair.Key, headerValue);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

    }
}
