using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib
{
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

        public IHttpProcessedParameter Get(RequestParameterType type, string name)
        {
            List<RequestParameter> list;
            _data.TryGetValue(GetKey(type, name), out list);
            var values = list == null ? new string[0] : list.Select(p => p.Value).ToArray();
            return new HttpProcessedParameter(type, name, values);
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

        public IEnumerator<RequestParameter> GetEnumerator()
        {
            return _data.Values.SelectMany(s => s).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class HttpProcessedParameter : IHttpProcessedParameter
    {
        private readonly RequestParameterType _type;
        private readonly string _name;
        private readonly string _singleValue;
        private IList<string> _values;

        public HttpProcessedParameter(RequestParameterType type, string name, string singleValue)
        {
            _type = type;
            _name = name;
            _singleValue = singleValue;
            _values = null;
        }

        public HttpProcessedParameter(RequestParameterType type, string name, IList<string> values)
        {
            _type = type;
            _name = name;
            _singleValue = values == null || values.Count == 0 ? null : values[0];
            _values = values;
        }

        public IHttpTypedProcessedParameter<int> AsInteger()
        {
            int parsedValue;
            if (_singleValue == null)
                return HttpTypedProcessedParameter<int>.CreateEmpty(_type, _name);
            else if (int.TryParse(_singleValue, out parsedValue))
                return HttpTypedProcessedParameter<int>.CreateParsed(_type, _name, parsedValue);
            else
                throw new ArgumentOutOfRangeException(_name, _singleValue, string.Format("Parameter {0} is in wrong format: {1}", _name, _singleValue));
        }

        public IHttpTypedProcessedParameter<string> AsString()
        {
            if (string.IsNullOrEmpty(_singleValue))
                return HttpTypedProcessedParameter<string>.CreateEmpty(_type, _name);
            else
                return HttpTypedProcessedParameter<string>.CreateParsed(_type, _name, _singleValue);
        }

        public IHttpTypedProcessedParameter<T> As<T>(Func<string, T> converter)
        {
            if (string.IsNullOrEmpty(_singleValue))
                return HttpTypedProcessedParameter<T>.CreateEmpty(_type, _name);
            try
            {
                T parsedValue = converter(_singleValue);
                return HttpTypedProcessedParameter<T>.CreateParsed(_type, _name, parsedValue);
            }
            catch (Exception)
            {
                throw new ArgumentOutOfRangeException(_name, _singleValue, string.Format("Parameter {0} is in wrong format: {1}", _name, _singleValue));
            }
        }

        public IList<string> GetRawValues()
        {
            if (_values == null)
                _values = (_singleValue == null)? new string[0] :  new[] { _singleValue };
            return _values;
        }

        public RequestParameterType Type
        {
            get { return _type; }
        }

        public string Name
        {
            get { return _name; }
        }
    }

    public class HttpTypedProcessedParameter<T> : IHttpTypedProcessedParameter<T>
    {
        private readonly RequestParameterType _type;
        private readonly string _name;
        private T _parsedValue;
        private T _defaultValue;
        private bool _hasDefault;
        private bool _isEmpty;

        private HttpTypedProcessedParameter(RequestParameterType type, string name)
        {
            _type = type;
            _name = name;
            _hasDefault = true;
        }

        public static IHttpTypedProcessedParameter<T> CreateEmpty(RequestParameterType type, string name)
        {
            return new HttpTypedProcessedParameter<T>(type, name)
            {
                _isEmpty = true
            };
        }

        public static IHttpTypedProcessedParameter<T> CreateParsed(RequestParameterType type, string name, T parsedValue)
        {
            return new HttpTypedProcessedParameter<T>(type, name)
            {
                _parsedValue = parsedValue
            };
        }

        public IHttpTypedProcessedParameter<T> Validate(Action<T> validator)
        {
            if (!_isEmpty)
                validator(_parsedValue);
            return this;
        }

        public IHttpTypedProcessedParameter<T> Default(T defaultValue)
        {
            _hasDefault = true;
            _defaultValue = defaultValue;
            return this;
        }

        public IHttpTypedProcessedParameter<T> Mandatory()
        {
            _hasDefault = false;
            return this;
        }

        public T Get()
        {
            if (!_isEmpty)
                return _parsedValue;
            else if (_hasDefault)
                return _defaultValue;
            else
                throw new MissingMandatoryParameterException(_name);
        }

        public RequestParameterType Type
        {
            get { return _type; }
        }

        public string Name
        {
            get { return _name; }
        }

        public bool IsEmpty
        {
            get { return _isEmpty; }
        }

        public IHttpTypedProcessedParameter<T> Validate(Predicate<T> predicate, string description)
        {
            if (!_isEmpty && !predicate(_parsedValue))
                throw new ParameterValidationException(_name, _parsedValue.ToString(), description);
            return this;
        }

        public IHttpTypedProcessedParameter<T> Validate(Action<IHttpTypedProcessedParameter<T>, T> validation)
        {
            if (!_isEmpty)
                validation(this, _parsedValue);
            return this;
        }
    }
}
