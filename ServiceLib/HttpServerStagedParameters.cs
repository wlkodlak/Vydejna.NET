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

        public IEnumerator<RequestParameter> GetEnumerator()
        {
            return _data.Values.SelectMany(s => s).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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
                return HttpTypedProcessedParameter<string>.CreateParsed(_type, _name, stringValue);
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
            _hasDefault = true;
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

        public ITypedProcessedParameter<T> Validate(Action<T> validator)
        {
            if (!_isEmpty)
                validator(_parsedValue);
            return this;
        }

        public ITypedProcessedParameter<T> Default(T defaultValue)
        {
            _hasDefault = true;
            _defaultValue = defaultValue;
            return this;
        }

        public ITypedProcessedParameter<T> Mandatory()
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
                throw new ArgumentNullException(_name, string.Format("Parameter {0} is not present", _name));
        }
    }
}
