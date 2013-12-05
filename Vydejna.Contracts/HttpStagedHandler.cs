using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public interface IHttpRequestDecoder
    {
        Task<HttpServerRequest> Process(HttpServerRequest request);
    }

    public interface IHttpRequestEnhancer
    {
        Task<HttpServerRequest> Process(HttpServerRequest request);
    }

    public interface IHttpPreprocessor
    {
        Task<HttpServerRequest> Process(HttpServerRequest request);
    }

    public interface IHttpProcessor
    {
        Task<object> Process(HttpServerRequest request);
    }

    public interface IHttpPostprocessor
    {
        Task<object> Process(HttpServerRequest request, object response);
    }

    public interface IHttpRequestEncoder
    {
        Task<HttpServerResponse> Process(HttpServerResponse request);
    }

    public interface IHttpStagedHandlerBuilder
    {
        IHttpStagedHandlerBuilder Add(IHttpRequestDecoder component);
        IHttpStagedHandlerBuilder Add(IHttpRequestEnhancer component);
        IHttpStagedHandlerBuilder Add(IHttpInputProcessor component);
        IHttpStagedHandlerBuilder Add(IHttpPreprocessor component);
        IHttpStagedHandlerBuilder Add(IHttpProcessor component);
        IHttpStagedHandlerBuilder Add(IHttpPostprocessor component);
        IHttpStagedHandlerBuilder Add(IHttpOutputProcessor component);
        IHttpStagedHandlerBuilder Add(IHttpRequestEncoder component);
        IHttpRouteHandler Build();
    }
}
