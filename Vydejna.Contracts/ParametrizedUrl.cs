using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class ParametrizedUrl
    {
        private string _originalUrl;
        private string _urlBase;
        private List<IPart> _parts;
        private string _queryString;

        private interface IPart
        {
            void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters);
        }

        private class FixedPart : IPart
        {
            private string _value;

            public FixedPart(string value)
            {
                _value = value;
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                builder.Append(_value);
            }
        }

        private class VariablePart : IPart
        {
            private string _name;

            public VariablePart(string name)
            {
                _name = name;
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                builder.Append(pathParameters[_name].Single());
            }
        }

        private class CompositePart : IPart
        {
            private List<IPart> subParts;

            public CompositePart(List<IPart> subParts)
            {
                this.subParts = subParts;
            }

            public void CompleteUrl(StringBuilder builder, ILookup<string, string> pathParameters)
            {
                foreach (var part in subParts)
                    part.CompleteUrl(builder, pathParameters);
            }
        }


        public ParametrizedUrl(string url)
        {
            _originalUrl = url;
            _parts = new List<IPart>();
            var regex = new Regex(@"^(([a-z]+://)?([^/]+)?/)(([^/]+)/)*([^?]*)(\?.*)?$");
            var match = regex.Match(url);
            if (!match.Success)
                throw new ArgumentException(string.Format("String is not URL: {0}", url));
            _urlBase = match.Groups[1].Value;
            foreach (Capture capture in match.Groups[5].Captures)
                _parts.Add(CreatePart(capture.Value));
            if (match.Groups[6].Success)
                _parts.Add(CreatePart(match.Groups[6].Value));
            _queryString = match.Groups[7].Success ? match.Groups[7].Value : "";
        }

        public override string ToString()
        {
            return _originalUrl;
        }

        private IPart CreatePart(string partString)
        {
            var regex = new Regex(@"^(({[^}]+})|([^{]+))*$");
            var match = regex.Match(partString);
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

        public string CompleteUrl(IEnumerable<RequestParameter> parameters)
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
            bool first = string.IsNullOrEmpty(_queryString);
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
    }
}
