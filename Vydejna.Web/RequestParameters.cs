using System;
using System.Collections.Generic;

namespace Vydejna.Web
{
    public interface IRequestParameters : IEnumerable<RequestParameterRaw>
    {
        IRequestParameter Get(string name);
        IRequestParameter Post(string name);
        IRequestParameter Header(string name);
    }

    public class RequestParameterRaw
    {
        public string Source;
        public string Name;
        public object Value;
    }

    public interface IRequestParameter
    {
        IRequestParameter<int> AsInteger();
        IRequestParameter<string> AsString();
        IRequestParameter<DateTime> AsDateTime();
        IRequestParameter<double> AsDouble();
        IRequestParameter<IRequestFile> AsFile();
        IRequestParameter<byte[]> AsBytes();
        IRequestParameter<T> As<T>(Func<string, T> parser);
    }

    public interface IRequestFile
    {
        string Filename { get; }
        int ContentLength { get; }
        System.IO.Stream Contents { get; }
    }

    public interface IRequestParameter<T>
    {
        T WithDefault(T defaultValue);
        T Mandatory();
        T Optional();
    }

    public interface IPostData
    {
        System.Xml.Linq.XElement Xml();
        System.IO.Stream Stream();
        T Parse<T>(Func<IRequestParameters, IPostData, T> parser);
    }

    public interface IRequestRoutePart
    {
        bool Matches(string segment);
        void Collect(ICollection<RequestParameterRaw> collection);
    }

    public interface IRequestRouter
    {
        void AddRoute(IRequestRoutePart[] parts, IRoutedHttpHandler handler);
        IRequestRouterConfiguration Route(string path);
        IRequestRouterConfiguration Route(params IRequestRoutePart[] parts);
    }

    public interface IRequestRouterConfiguration
    {
        void To(IRoutedHttpHandler handler);
    }

    public interface IRoutedHttpHandler
    {
        IFinalResponse ProcessRequest(IRequestParameters parameters, IPostData postData);
    }

    public interface IFinalResponse
    {
        int StatusCode { get; }
        ICollection<RequestParameterRaw> Headers { get; }
        System.IO.Stream Contents { get; }
    }

    public interface IResponse
    {
        ResponseType Type { get; }
        ICollection<RequestParameterRaw> Headers { get; }
        object ValueToSerialize { get; }
        Exception Exception { get; }
        int StatusCode { get; }
    }

    public enum ResponseType
    {
        Raw,
        Json,
        Xml,
        ObjectToSerialize,
        Exception,
        Redirect,
        Error
    }
}