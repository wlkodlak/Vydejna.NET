using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;

namespace ServiceLib
{
    public interface IHttpServerRawContext
    {
        string Method { get; }
        string Url { get; }
        string ClientAddress { get; }
        int StatusCode { get; set; }
        Stream InputStream { get; }
        Stream OutputStream { get; }
        IList<RequestParameter> RouteParameters { get; }
        IHttpServerRawHeaders InputHeaders { get; }
        IHttpServerRawHeaders OutputHeaders { get; }
        void Close();
    }
    public interface IHttpServerRawHeaders : IEnumerable<KeyValuePair<string, string>>
    {
        void Add(string name, string value);
        void Clear();
    }

    public interface IHttpServerStagedContext
    {
        string Method { get; }
        string Url { get; }
        string ClientAddress { get; }
        string InputString { get; }
        int StatusCode { get; set; }
        string OutputString { get; set; }
        IHttpSerializer InputSerializer { get; }
        IHttpSerializer OutputSerializer { get; }
        IHttpServerStagedHeaders InputHeaders { get; }
        IHttpServerStagedHeaders OutputHeaders { get; }
        IEnumerable<RequestParameter> RawParameters { get; }
        IHttpProcessedParameter Parameter(string name);
        IHttpProcessedParameter PostData(string name);
        IHttpProcessedParameter Route(string name);
        void Close();
    }
    public interface IHttpServerStagedHeaders : IHttpServerRawHeaders
    {
        string ContentType { get; set; }
        IHttpServerStagedWeightedHeader AcceptTypes { get; }
        IHttpServerStagedWeightedHeader AcceptLanguages { get; }
        int ContentLength { get; set; }
        string Referer { get; set; }
        string Location { get; set; }
    }
    public interface IHttpServerStagedWeightedHeader : IEnumerable<string>
    {
        string Name { get; }
        int Count { get; }
        string RawValue { get; set; }
        string this[int index] { get; }
        void Clear();
        void Add(string value);
        bool IsSet { get; }
    }
    public interface IHttpProcessedParameter
    {
        RequestParameterType Type { get; }
        string Name { get; }
        IList<string> GetRawValues();
        IHttpTypedProcessedParameter<int> AsInteger();
        IHttpTypedProcessedParameter<string> AsString();
        IHttpTypedProcessedParameter<T> As<T>(Func<string, T> converter);
    }
    public interface IHttpTypedProcessedParameter<T>
    {
        RequestParameterType Type { get; }
        string Name { get; }
        bool IsEmpty { get; }
        IHttpTypedProcessedParameter<T> Validate(Predicate<T> predicate, string description);
        IHttpTypedProcessedParameter<T> Validate(Action<IHttpTypedProcessedParameter<T>, T> validation);
        IHttpTypedProcessedParameter<T> Default(T defaultValue);
        IHttpTypedProcessedParameter<T> Mandatory();
        T Get();
    }
    public class MissingMandatoryParameterException : ArgumentException
    {
        public MissingMandatoryParameterException(string paramName)
            : base(string.Format("Mandatory parameter {0} is missing", paramName), paramName)
        {
        }
    }
    public class ParameterValidationException : ArgumentException
    {
        public ParameterValidationException(string paramName, string actualValue, string description)
            : base(CreateMessage(paramName, actualValue, description), paramName)
        {
            ActualValue = actualValue;
            Description = description;
        }

        private static string CreateMessage(string paramName, string actualValue, string description)
        {
            return string.Format("Parameter {0} {1}. It's {2}.", paramName, description, actualValue);
        }

        public string ActualValue { get; private set; }
        public string Description { get; private set; }
    }
}
