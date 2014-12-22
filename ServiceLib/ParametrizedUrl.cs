using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ServiceLib
{
    public class ParametrizedUrl
    {
        private readonly string _originalUrl;
        private readonly string _urlBase;
        private readonly List<IPart> _parts;
        private readonly string _queryString;
        private readonly ParametrizedUrlParts _prefix;

        private interface IPart
        {
            void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters);
            void Match(string urlPart, ParametrizedUrlMatch result, bool updateScore);
            void BuildRegex(StringBuilder pattern);
            bool HasRegexGroup { get; }
            bool BuildPrefix(List<string> listFixed);
        }

        private class FixedPart : IPart
        {
            private readonly string _value;

            public FixedPart(string value)
            {
                _value = value;
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                builder.Append(_value);
            }

            public void Match(string urlPart, ParametrizedUrlMatch result, bool updateScore)
            {
                if (urlPart != _value)
                    result.Fail();
                else if (updateScore)
                    result.UpdateScore(3);
            }

            public void BuildRegex(StringBuilder pattern)
            {
                pattern.Append(Regex.Escape(_value));
            }

            public bool HasRegexGroup
            {
                get { return false; }
            }

            public bool BuildPrefix(List<string> listFixed)
            {
                if (string.IsNullOrEmpty(_value))
                    return false;
                listFixed.Add(_value);
                return true;
            }
        }

        private class VariablePart : IPart
        {
            private readonly string _name;

            public VariablePart(string name)
            {
                _name = name;
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                builder.Append(pathParameters[_name].Single());
            }


            public void Match(string urlPart, ParametrizedUrlMatch result, bool updateScore)
            {
                result.AddParameter(_name, urlPart);
                if (updateScore)
                    result.UpdateScore(string.IsNullOrEmpty(urlPart) ? 0 : 1);
            }

            public void BuildRegex(StringBuilder pattern)
            {
                pattern.Append("(.*)");
            }

            public bool HasRegexGroup
            {
                get { return true; }
            }

            public bool BuildPrefix(List<string> listFixed)
            {
                return false;
            }
        }

        private class CompositePart : IPart
        {
            private readonly List<IPart> _subParts;
            private readonly Regex _regex;

            public CompositePart(List<IPart> subParts)
            {
                _subParts = subParts;
                var pattern = new StringBuilder();
                pattern.Append("^");
                foreach (var part in subParts)
                    part.BuildRegex(pattern);
                pattern.Append("$");
                _regex = new Regex(pattern.ToString(), RegexOptions.Compiled);
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                foreach (var part in _subParts)
                    part.CompleteUrl(builder, pathParameters);
            }

            public void Match(string urlPart, ParametrizedUrlMatch result, bool updateScore)
            {
                var match = _regex.Match(urlPart);
                if (!match.Success)
                    result.Fail();
                else
                {
                    result.UpdateScore(2);
                    int groupIndex = 1;
                    foreach (var part in _subParts)
                    {
                        if (part.HasRegexGroup)
                        {
                            part.Match(match.Groups[groupIndex].Value, result, false);
                            groupIndex++;
                        }
                    }
                }
            }

            public void BuildRegex(StringBuilder pattern)
            {
                throw new NotSupportedException();
            }

            public bool HasRegexGroup
            {
                get { return false; }
            }

            public bool BuildPrefix(List<string> listFixed)
            {
                return false;
            }
        }

        private static readonly Regex RegexWholeUrl = new Regex(
            @"^(([a-z]+://)?([^/]+)?/)(([^/]+)/)*([^?]*)(\?.*)?$", RegexOptions.Compiled);

        public ParametrizedUrl(string url)
        {
            _originalUrl = url;
            _parts = new List<IPart>();
            var match = RegexWholeUrl.Match(url);
            if (!match.Success)
                throw new ArgumentException(string.Format("String is not URL: {0}", url));
            _urlBase = match.Groups[1].Value;
            foreach (Capture capture in match.Groups[5].Captures)
                _parts.Add(CreatePart(capture.Value));
            if (match.Groups[6].Success)
                _parts.Add(CreatePart(match.Groups[6].Value));
            _queryString = match.Groups[7].Success ? match.Groups[7].Value : "";
            var listFixed = new List<string>(_parts.Count);
            foreach (var item in _parts)
            {
                if (!item.BuildPrefix(listFixed))
                    break;
            }
            _prefix = new ParametrizedUrlParts(listFixed);
        }

        public override string ToString()
        {
            return _originalUrl;
        }

        public ParametrizedUrlParts Prefix
        {
            get { return _prefix; }
        }

        private static readonly Regex RegexCreatePart = new Regex(@"^(({[^}]+})|([^{]+))*$", RegexOptions.Compiled);

        private IPart CreatePart(string partString)
        {
            var match = RegexCreatePart.Match(partString);
            var subParts = new List<IPart>();
            foreach (Capture capture in match.Groups[1].Captures)
            {
                var element = capture.Value;
                if (element.StartsWith("{"))
                    subParts.Add(new VariablePart(element.Substring(1, element.Length - 2)));
                else
                    subParts.Add(new FixedPart(element));
            }
            if (subParts.Count == 0)
                return new FixedPart("");
            else if (subParts.Count == 1)
                return subParts[0];
            else
                return new CompositePart(subParts);
        }

        public string CompleteUrl(IList<RequestParameter> parameters)
        {
            var builder = new StringBuilder();
            builder.Append(_urlBase);
            BuildPath(parameters, builder);
            BuildQueryString(parameters, builder);
            return builder.ToString();
        }

        private void BuildPath(IEnumerable<RequestParameter> parameters, StringBuilder builder)
        {
            var pathParameters = parameters
                .Where(p => p.Type == RequestParameterType.Path)
                .ToLookup(p => p.Name, p => p.Value);
            var first = true;
            foreach (var part in _parts)
            {
                if (first)
                    first = false;
                else
                    builder.Append("/");
                part.CompleteUrl(builder, pathParameters);
            }
        }

        private void BuildQueryString(IEnumerable<RequestParameter> parameters, StringBuilder builder)
        {
            var first = string.IsNullOrEmpty(_queryString);
            builder.Append(_queryString);
            foreach (var parameter in parameters)
            {
                if (parameter.Type == RequestParameterType.QueryString)
                {
                    builder.Append(first ? "?" : "&");
                    first = false;
                    builder.Append(Uri.EscapeDataString(parameter.Name));
                    builder.Append("=");
                    builder.Append(Uri.EscapeDataString(parameter.Value));
                }
            }
        }

        public ParametrizedUrlMatch Match(ParametrizedUrlParts url)
        {
            var result = new ParametrizedUrlMatch();
            var count = Math.Max(url.Count, _parts.Count);
            for (int i = 0; i < count && result.Success; i++)
            {
                if (i < _parts.Count)
                    _parts[i].Match(i < url.Count ? url[i] : string.Empty, result, true);
                else if (!string.IsNullOrEmpty(url[i]))
                    result.Fail();
                else if (i != (count - 1))
                    result.Fail();
            }
            return result;
        }

        public static ParametrizedUrlParts UrlForMatching(string url)
        {
            var parts = new Uri(url).AbsolutePath.Split('/');
            return new ParametrizedUrlParts(parts.Skip(1).ToArray());
        }

        public static IEnumerable<RequestParameter> ParseQueryString(string url)
        {
            var parameters = new Uri(url).Query.TrimStart('?').Split('&');
            foreach (var parameter in parameters)
            {
                if (string.IsNullOrEmpty(parameter))
                    continue;
                var parts = parameter.Split(new[] {'='}, 2);
                var name = parts[0];
                var value = parts.Length == 1 ? name : UnescapeUri(parts[1]);
                if (!string.IsNullOrEmpty(name))
                    yield return new RequestParameter(RequestParameterType.QueryString, name, value);
            }
        }

        private static string UnescapeUri(string s)
        {
            return Uri.UnescapeDataString(s.Replace('+', ' '));
        }
    }

    public class ParametrizedUrlMatch : IEnumerable<RequestParameter>
    {
        private int _score;
        private readonly List<RequestParameter> _parameters;

        public ParametrizedUrlMatch()
        {
            _parameters = new List<RequestParameter>();
        }

        public bool Success
        {
            get { return _score >= 0; }
        }

        public int Score
        {
            get { return _score; }
        }

        public int Count
        {
            get { return _parameters.Count; }
        }

        public void AddParameter(string name, string value)
        {
            if (_score < 0)
                return;
            _parameters.Add(new RequestParameter(RequestParameterType.Path, name, value));
        }

        public void UpdateScore(int value)
        {
            if (_score < 0)
                return;
            _score = GetScoreUpdate(_score, value);
        }

        public static int GetScoreUpdate(int score, int value)
        {
            return score * 10 + value;
        }

        public ParametrizedUrlMatch Fail()
        {
            _parameters.Clear();
            _score = -1;
            return this;
        }

        public IEnumerator<RequestParameter> GetEnumerator()
        {
            return _parameters.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class ParametrizedUrlParts : IComparable<ParametrizedUrlParts>
    {
        private readonly IList<string> _elements;

        public ParametrizedUrlParts(params string[] elements)
        {
            _elements = elements;
        }

        public ParametrizedUrlParts(IList<string> elements)
        {
            _elements = elements;
        }

        public int Count
        {
            get { return _elements.Count; }
        }

        public string this[int index]
        {
            get { return _elements[index]; }
        }

        public int CompareTo(ParametrizedUrlParts other)
        {
            if (other == null)
                return 1;
            if (ReferenceEquals(this, other))
                return 0;
            int countA = _elements.Count;
            int countB = other._elements.Count;
            int countMax = Math.Max(countA, countB);
            for (int i = 0; i < countMax; i++)
            {
                var presentA = i < countA;
                var presentB = i < countB;
                if (!presentA)
                    return -1;
                if (!presentB)
                    return 1;
                var partA = _elements[i];
                var partB = other._elements[i];
                var comparison = string.CompareOrdinal(partA, partB);
                if (comparison < 0)
                    return -2;
                else if (comparison > 0)
                    return 2;
            }
            return 0;
        }

        public override string ToString()
        {
            if (_elements.Count == 0)
                return "/";
            var sb = new StringBuilder();
            foreach (var element in _elements)
                sb.Append("/").Append(element);
            return sb.ToString();
        }

        public override int GetHashCode()
        {
            return (_elements.Count == 0) ? 4987234 : _elements[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var oth = obj as ParametrizedUrlParts;
            return oth != null && CompareTo(oth) == 0;
        }

        public bool IsPrefixOf(ParametrizedUrlParts other)
        {
            var comparison = CompareTo(other);
            return comparison == 0 || comparison == -1;
        }
    }
}