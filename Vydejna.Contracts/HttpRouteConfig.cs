using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

namespace Vydejna.Contracts
{
    public interface IHttpRouteConfigCommitable
    {
        void Commit();
    }
    public interface ISerializerPicker
    {
        IHttpSerializer PickDeserializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);
        IHttpSerializer PickSerializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);
    }
    public interface IHttpProcessor
    {
        Task Process(IHttpServerStagedContext context);
    }

    public interface IHttpRouteCommonConfigurator : IHttpRouteConfigCommitable
    {
        IHttpRouteCommonConfigurator SubRouter();
        IHttpRouteCommonConfigurator ForFolder(string path);
        IHttpRouteCommonConfigurator WithPicker(ISerializerPicker picker);
        IHttpRouteCommonConfigurator WithSerializer(IHttpSerializer serializer);
        IHttpRouteCommonConfiguratorRoute Route(string path);
    }
    public interface IHttpRouteCommonConfiguratorRoute
    {
        IHttpRouteCommonConfiguratorRoute WithPicker(ISerializerPicker picker);
        IHttpRouteCommonConfiguratorRoute WithSerializer(IHttpSerializer serializer);
        IHttpRouteConfigCommitable To(IHttpRouteHandler handler);
        IHttpRouteConfigCommitable To(IHttpProcessor processor);
    }
    public interface IHttpRouteCommonConfiguratorExtended : IHttpRouteCommonConfigurator
    {
        void Register(IHttpRouteConfigCommitable subRouter);
        IHttpAddRoute GetRouter();
        string GetPath();
        ISerializerPicker GetPicker();
        IList<IHttpSerializer> GetSerializers();
    }

    public class HttpRouterCommon : IHttpRouteCommonConfiguratorExtended
    {
        private string _prefix;
        private IHttpAddRoute _router;
        private List<IHttpRouteConfigCommitable> _subRouters;
        private List<IHttpRouteConfigCommitable> _pending;
        private ISerializerPicker _picker;
        private IList<IHttpSerializer> _serializers;

        public HttpRouterCommon(IHttpAddRoute router)
        {
            _prefix = "";
            _router = router;
            _subRouters = new List<IHttpRouteConfigCommitable>();
            _pending = new List<IHttpRouteConfigCommitable>();
            _picker = null;
            _serializers = new List<IHttpSerializer>();
        }

        public HttpRouterCommon(IHttpRouteCommonConfiguratorExtended parent, string path)
        {
            parent.Register(this);
            _prefix = parent.GetPath();
            if (!string.IsNullOrEmpty(path))
                _prefix = string.Concat(_prefix, "/", path);
            _router = parent.GetRouter();
            _subRouters = new List<IHttpRouteConfigCommitable>();
            _pending = new List<IHttpRouteConfigCommitable>();
            _picker = parent.GetPicker();
            _serializers = parent.GetSerializers().ToList();
        }

        void IHttpRouteCommonConfiguratorExtended.Register(IHttpRouteConfigCommitable subRouter)
        {
            _subRouters.Add(subRouter);
        }

        IHttpAddRoute IHttpRouteCommonConfiguratorExtended.GetRouter()
        {
            return _router;
        }

        string IHttpRouteCommonConfiguratorExtended.GetPath()
        {
            return _prefix;
        }

        ISerializerPicker IHttpRouteCommonConfiguratorExtended.GetPicker()
        {
            return _picker;
        }

        IList<IHttpSerializer> IHttpRouteCommonConfiguratorExtended.GetSerializers()
        {
            return _serializers;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.SubRouter()
        {
            return new HttpRouterCommon(this, "");
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.ForFolder(string path)
        {
            _prefix = string.Concat(_prefix, "/", path);
            return this;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.WithPicker(ISerializerPicker picker)
        {
            if (_picker == null)
                _picker = picker;
            else
                _picker = new HttpSerializerPickerProxy(picker, _picker);
            return this;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.WithSerializer(IHttpSerializer serializer)
        {
            _serializers.Add(serializer);
            return this;
        }

        void IHttpRouteConfigCommitable.Commit()
        {
            foreach (var item in _subRouters)
                item.Commit();
            foreach (var item in _pending)
                item.Commit();
            _pending.Clear();
        }

        IHttpRouteCommonConfiguratorRoute IHttpRouteCommonConfigurator.Route(string path)
        {
            return new HttpRouterCommonRoute(this, path);
        }

        private class HttpRouterCommonRoute : IHttpRouteCommonConfiguratorRoute, IHttpRouteConfigCommitable
        {
            private HttpRouterCommon _parent;
            private bool _isCommitted;
            private string _path;
            private IHttpAddRoute _router;
            private ISerializerPicker _picker;
            private IList<IHttpSerializer> _serializers;
            private IHttpRouteHandler _routeHandler;

            public HttpRouterCommonRoute(HttpRouterCommon parent, string path)
            {
                _parent = parent;
                _router = parent._router;
                _picker = parent._picker;
                _serializers = parent._serializers.ToList();
                _path = string.Concat(parent._prefix, "/", path);
            }

            public IHttpRouteCommonConfiguratorRoute WithPicker(ISerializerPicker picker)
            {
                if (_picker == null)
                    _picker = picker;
                else
                    _picker = new HttpSerializerPickerProxy(picker, _picker);
                return this;
            }

            public IHttpRouteCommonConfiguratorRoute WithSerializer(IHttpSerializer serializer)
            {
                _serializers.Add(serializer);
                return this;
            }

            public IHttpRouteConfigCommitable To(IHttpRouteHandler handler)
            {
                _routeHandler = handler;
                _parent._pending.Add(this);
                return this;
            }

            public IHttpRouteConfigCommitable To(IHttpProcessor processor)
            {
                _routeHandler = new HttpRouteStagedHandler(_picker, _serializers.ToList(), processor);
                _parent._pending.Add(this);
                return this;
            }

            public void Commit()
            {
                _isCommitted = true;
                _router.AddRoute(_path, _routeHandler, null);
            }
        }
    }

    public class HttpRouteStagedHandler : IHttpRouteHandler
    {
        private ISerializerPicker _picker;
        private List<IHttpSerializer> _serializers;
        private IHttpProcessor _processor;

        public HttpRouteStagedHandler(ISerializerPicker picker, List<IHttpSerializer> serializers, IHttpProcessor processor)
        {
            this._picker = picker;
            this._serializers = serializers;
            this._processor = processor;
        }

        public Task Handle(IHttpServerRawContext context)
        {
            return new Worker(this, context).Execute();
        }

        private class Worker
        {
            private TaskCompletionSource<object> _task;
            private HttpRouteStagedHandler _parent;
            private IHttpServerRawContext _rawContext;
            private HttpServerStagedContext _staged;
            private StreamReader _inputReader;
            private StreamWriter _outputWriter;

            public Worker(HttpRouteStagedHandler parent, IHttpServerRawContext context)
            {
                _parent = parent;
                _rawContext = context;
                _staged = new HttpServerStagedContext(_rawContext);
                _task = new TaskCompletionSource<object>();
            }
            public Task Execute()
            {
                try
                {
                    _staged.LoadParameters();
                    if (_rawContext.InputStream != null)
                    {
                        _inputReader = new StreamReader(_rawContext.InputStream);
                        _inputReader.ReadToEndAsync().ContinueWith(Phase2);
                    }
                    else
                        Phase3();
                }
                catch (AggregateException ex)
                {
                    _task.SetException(ex.GetBaseException());
                }
                catch (Exception ex)
                {
                    _task.SetException(ex);
                }
                return _task.Task;
            }
            private void Phase2(Task<string> inputStringTask)
            {
                try
                {
                    try
                    {
                        _staged.InputString = inputStringTask.Result;
                    }
                    finally
                    {
                        _inputReader.Dispose();
                    }
                    Phase3();
                }
                catch (AggregateException ex)
                {
                    _task.SetException(ex.GetBaseException());
                }
                catch (Exception ex)
                {
                    _task.SetException(ex);
                }
            }
            private void Phase3()
            {
                _staged.InputSerializer = _parent._picker.PickDeserializer(_staged, _parent._serializers, null);
                _staged.OutputSerializer = _parent._picker.PickSerializer(_staged, _parent._serializers, null);
                _parent._processor.Process(_staged).ContinueWith(Phase4);
            }
            private void Phase4(Task task)
            {
                if (task.Exception != null)
                    _task.SetException(task.Exception.GetBaseException());
                else
                {
                    _rawContext.StatusCode = _staged.StatusCode;
                    _rawContext.OutputHeaders.Clear();
                    foreach (var item in _staged.OutputHeaders)
                        _rawContext.OutputHeaders.Add(item.Key, item.Value);
                    _outputWriter = new StreamWriter(_rawContext.OutputStream);
                    _outputWriter.WriteAsync(_staged.OutputString).ContinueWith(Phase5);
                }
            }
            private void Phase5(Task task)
            {
                if (task.Exception != null)
                    _task.SetException(task.Exception.GetBaseException());
                else
                    _task.SetResult(null);
            }
        }
    }
   
    public class HttpServerStagedContext : IHttpServerStagedContext
    {
        private IHttpServerRawContext _rawContext;
        private HttpServerStagedParameters _parameters;

        public HttpServerStagedContext(IHttpServerRawContext context)
        {
            _rawContext = context;
            Method = context.Method;
            Url = context.Url;
            ClientAddress = context.ClientAddress;
            InputHeaders = new HttpServerStagedContextHeaders();
            foreach (var item in context.InputHeaders)
                InputHeaders.Add(item.Key, item.Value);
            OutputHeaders = new HttpServerStagedContextHeaders();
            _parameters = new HttpServerStagedParameters();
        }

        public void LoadParameters()
        {
            foreach (var parameter in _rawContext.RouteParameters)
                _parameters.AddParameter(parameter);
            foreach (var parameter in ParametrizedUrl.ParseQueryString(_rawContext.Url))
                _parameters.AddParameter(parameter);
        }

        public string Method { get; private set; }
        public string Url { get; private set; }
        public string ClientAddress { get; private set; }
        public string InputString { get; set; }
        public int StatusCode { get; set; }
        public string OutputString { get; set; }
        public IHttpSerializer InputSerializer { get; set; }
        public IHttpSerializer OutputSerializer { get; set; }
        public IHttpServerStagedHeaders InputHeaders { get; private set; }
        public IHttpServerStagedHeaders OutputHeaders { get; private set; }
        public IEnumerable<RequestParameter> RawParameters
        {
            get { return _parameters; }
        }
        public IProcessedParameter Parameter(string name)
        {
            return _parameters.Get(RequestParameterType.QueryString, name);
        }
        public IProcessedParameter PostData(string name)
        {
            return _parameters.Get(RequestParameterType.PostData, name);
        }
        public IProcessedParameter Route(string name)
        {
            return _parameters.Get(RequestParameterType.Path, name);
        }
    }

    public class HttpServerStagedContextHeaders : IHttpServerStagedHeaders
    {
        private int _count;
        private string _contentType, _referer, _location;
        private int _contentLength;
        private HttpServerStagedContextWeightedHeader _acceptTypes, _acceptLanguages;
        private List<KeyValuePair<string, string>> _custom;

        public HttpServerStagedContextHeaders()
        {
            _contentLength = -1;
            _acceptTypes = new HttpServerStagedContextWeightedHeader("Accept");
            _acceptLanguages = new HttpServerStagedContextWeightedHeader("Accept-Languages");
            _custom = new List<KeyValuePair<string, string>>();
        }

        public string ContentType { get { return _contentType; } set { _contentType = value; } }
        public IHttpServerStagedWeightedHeader AcceptTypes { get { return _acceptTypes; } }
        public IHttpServerStagedWeightedHeader AcceptLanguages { get { return _acceptLanguages; } }
        public int ContentLength { get { return _contentLength; } set { _contentLength = value; } }
        public string Referer { get { return _referer; } set { _referer = value; } }
        public string Location { get { return _location; } set { _location = value; } }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return CreateList().GetEnumerator();
        }

        private List<KeyValuePair<string, string>> CreateList()
        {
            var list = new List<KeyValuePair<string, string>>();
            AddToList(list, "Content-Type", _contentType);
            AddToList(list, "Content-Length", _contentLength, -1);
            AddToList(list, "Referer", _referer);
            AddToList(list, "Location", _location);
            AddToList(list, _acceptTypes);
            AddToList(list, _acceptLanguages);
            list.AddRange(_custom);
            return list;
        }

        private void AddToList(List<KeyValuePair<string, string>> list, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
                list.Add(new KeyValuePair<string, string>(name, value));
        }
        private void AddToList(List<KeyValuePair<string, string>> list, string name, int value, int defaultValue)
        {
            if (value != defaultValue)
                list.Add(new KeyValuePair<string, string>(name, value.ToString()));
        }
        private void AddToList(List<KeyValuePair<string, string>> list, IHttpServerStagedWeightedHeader header)
        {
            if (header.IsSet)
                list.Add(new KeyValuePair<string, string>(header.Name, header.RawValue));
        }

        public void Add(string name, string value)
        {
            switch (name)
            {
                case "Accept":
                    _acceptTypes.RawValue = value;
                    break;
                case "Accept-Language":
                    _acceptTypes.RawValue = value;
                    break;
                case "Referer":
                    _referer = value;
                    break;
                case "Location":
                    _location = value;
                    break;
                case "Content-Type":
                    _contentType = value;
                    break;
                case "Content-Length":
                    if (!int.TryParse(value, out _contentLength))
                        _contentLength = -1;
                    break;
                default:
                    _custom.Add(new KeyValuePair<string, string>(name, value));
                    break;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Clear()
        {
            _custom.Clear();
            _acceptTypes.Clear();
            _acceptLanguages.Clear();
            _referer = _location = _contentType = null;
            _contentLength = -1;
        }
    }

    public class HttpServerStagedContextWeightedHeader : IHttpServerStagedWeightedHeader
    {
        private string _name;
        private bool _isSet;
        private string _rawValue;
        private List<string> _values;

        public HttpServerStagedContextWeightedHeader(string name)
        {
            _name = name;
            _isSet = false;
            _rawValue = null;
            _values = new List<string>();
        }
        public string Name
        {
            get { return _name; }
        }
        public string RawValue
        {
            get { return _rawValue; }
            set { SetRawValue(value); }
        }
        public int Count
        {
            get { return _values.Count; }
        }
        public string this[int index]
        {
            get { return _values[index]; }
        }
        public bool IsSet
        {
            get { return _isSet; }
        }

        public void Clear()
        {
            _isSet = false;
            _rawValue = "";
            _values.Clear();
        }
        public void Add(string value)
        {
            _isSet = true;
            if (string.IsNullOrEmpty(_rawValue))
                _rawValue = value;
            else
                _rawValue = string.Concat(_rawValue, ", ", value);
            _values.Add(value);
        }
        public void SetRawValue(string rawValue)
        {
            _rawValue = rawValue;
            _values.Clear();
            _isSet = true;
            if (!string.IsNullOrEmpty(rawValue))
            {
                foreach (var element in rawValue.Split(','))
                {
                    var elementParts = element.Split(';');
                    _values.Add(elementParts[0].Trim());
                }
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            return _values.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class HttpServerStagedParameters : IEnumerable<RequestParameter>
    {
        private Dictionary<string, List<RequestParameter>> _data;

        public HttpServerStagedParameters()
        {
            _data = new Dictionary<string, List<RequestParameter>>();
        }

        public void AddParameter(RequestParameter parameter)
        {
            List<RequestParameter> list;
            var key = GetKey(parameter.Type, parameter.Name);
            if (!_data.TryGetValue(key, out list))
                _data[key] = list = new List<RequestParameter>();
            list.Add(parameter);
        }

        public IProcessedParameter Get(RequestParameterType type, string name)
        {
            List<RequestParameter> list;
            _data.TryGetValue(GetKey(type, name), out list);
            return new HttpProcessedParameter(type, name, list);
        }

        private string GetKey(RequestParameterType type, string name)
        {
            switch (type)
            {
                case RequestParameterType.QueryString:
                    return "Q" + name;
                case RequestParameterType.PostData:
                    return "I" + name;
                case RequestParameterType.Path:
                    return "R" + name;
                default:
                    return "!" + name;
            }
        }
    }

    public class HttpProcessedParameter : IProcessedParameter
    {
        private RequestParameterType _type;
        private string _name;
        private List<string> _values;

        public HttpProcessedParameter(RequestParameterType type, string name, IEnumerable<RequestParameter> values)
        {
            _type = type;
            _name = name;
            _values = values.Select(p => p.Value).Where(v => !string.IsNullOrEmpty(v)).ToList();
        }

        public ITypedProcessedParameter<int> AsInteger()
        {
            var stringValue = _values.FirstOrDefault();
            int parsedValue;
            if (stringValue == null)
                return HttpTypedProcessedParameter<int>.CreateEmpty(_type, _name);
            else if (int.TryParse(stringValue, out parsedValue))
                return HttpTypedProcessedParameter<int>.CreateParsed(_type, _name, parsedValue);
            else
                throw new ArgumentOutOfRangeException(_name, stringValue, string.Format("Parameter {0} is in wrong format: {1}", _name, stringValue));
        }

        public ITypedProcessedParameter<string> AsString()
        {
            var stringValue = _values.FirstOrDefault();
            if (stringValue == null)
                return HttpTypedProcessedParameter<string>.CreateEmpty(_type, _name);
            else
                return HttpTypedProcessedParameter<string>.CreateParsed(_type, _name, parsedValue);
        }

        public ITypedProcessedParameter<T> As<T>(Func<string, T> converter)
        {
            var stringValue = _values.FirstOrDefault();
            if (stringValue == null)
                return HttpTypedProcessedParameter<T>.CreateEmpty(_type, _name);
            try
            {
                T parsedValue = converter(stringValue);
                return HttpTypedProcessedParameter<T>.CreateParsed(_type, _name, parsedValue);
            }
            catch (Exception)
            {
                throw new ArgumentOutOfRangeException(_name, stringValue, string.Format("Parameter {0} is in wrong format: {1}", _name, stringValue));
            }
        }
    }

    public class HttpTypedProcessedParameter<T> : ITypedProcessedParameter<T>
    {
        private RequestParameterType _type;
        private string _name;
        private T _parsedValue;
        private T _defaultValue;
        private bool _hasDefault;
        private bool _isEmpty;

        private HttpTypedProcessedParameter(RequestParameterType type, string name)
        {
            _type = type;
            _name = name;
        }

        public static ITypedProcessedParameter<T> CreateEmpty(RequestParameterType type, string name)
        {
            return new HttpTypedProcessedParameter<T>(type, name)
            {
                _isEmpty = true
            };
        }

        public static ITypedProcessedParameter<T> CreateParsed(RequestParameterType type, string name, T parsedValue)
        {
            return new HttpTypedProcessedParameter<T>(type, name)
            {
                _parsedValue = parsedValue
            };
        }

        public ITypedProcessedParameter<T> WithValidator(Action<T> validator)
        {
            if (!_isEmpty)
                validator(_parsedValue);
            return this;
        }

        public ITypedProcessedParameter<T> WithDefault(T defaultValue)
        {
            _hasDefault = true;
            _defaultValue = defaultValue;
            return this;
        }

        public T Get()
        {
            if (!_isEmpty)
                return _parsedValue;
            else if (_hasDefault)
                return _defaultValue;
            else
                throw new ArgumentNullException(_name, string.Format("Parameter {0} is not present", _name));
        }
    }
}
