using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;

namespace Vydejna.Contracts
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
        IProcessedParameter Parameter(string name);
        IProcessedParameter PostData(string name);
        IProcessedParameter Route(string name);
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
    public interface IProcessedParameter
    {
        ITypedProcessedParameter<int> AsInteger();
        ITypedProcessedParameter<string> AsString();
        ITypedProcessedParameter<T> As<T>(Func<string, T> converter);
    }
    public interface ITypedProcessedParameter<T>
    {
        ITypedProcessedParameter<T> Validate(Action<T> validator);
        ITypedProcessedParameter<T> Default(T defaultValue);
        ITypedProcessedParameter<T> Mandatory();
        T Get();
    }
}
