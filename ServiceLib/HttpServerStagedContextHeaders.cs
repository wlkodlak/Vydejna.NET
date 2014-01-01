using System.Collections.Generic;

namespace ServiceLib
{
    public class HttpServerStagedContextHeaders : IHttpServerStagedHeaders
    {
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

}
