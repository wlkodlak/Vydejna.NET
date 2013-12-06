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
        Task<HttpServerRequest> Process(HttpServerRequest request, IEnumerable<RequestParameter> routeParameters);
    }

    public interface IHttpPreprocessor
    {
        Task<object> Process(HttpServerRequest request);
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

    public class HttpStagedHandler : IHttpRouteHandler, IHttpStagedHandlerBuilder
    {
        private List<IHttpRequestDecoder> _decoders = new List<IHttpRequestDecoder>();
        private List<IHttpRequestEnhancer> _enhancers = new List<IHttpRequestEnhancer>();
        private List<IHttpInputProcessor> _inputs = new List<IHttpInputProcessor>();
        private List<IHttpPreprocessor> _pre = new List<IHttpPreprocessor>();
        private IHttpProcessor _processor;
        private List<IHttpPostprocessor> _post = new List<IHttpPostprocessor>();
        private List<IHttpOutputProcessor> _outputs = new List<IHttpOutputProcessor>();
        private List<IHttpRequestEncoder> _encoders = new List<IHttpRequestEncoder>();

        public static IHttpStagedHandlerBuilder CreateBuilder()
        {
            return new HttpStagedHandler();
        }

        public async Task<HttpServerResponse> Handle(HttpServerRequest request, IList<RequestParameter> routeParameters)
        {
            try
            {
                foreach (var handler in _decoders)
                    request = await handler.Process(request);
                foreach (var handler in _enhancers)
                    request = await handler.Process(request, routeParameters);
                var contentType = request.Headers.ContentType;
                foreach (var handler in _inputs)
                {
                    if (!handler.HandlesContentType(contentType))
                        continue;
                    request.PostDataObject = await handler.ProcessInput(request);
                    break;
                }
                var result = (object)null;
                try
                {
                    foreach (var handler in _pre)
                        request.ContextObject = await handler.Process(request);
                    result = await _processor.Process(request);
                    foreach (var handler in _post)
                        result = await handler.Process(request, result);
                }
                catch (Exception ex)
                {
                    result = ex;
                }
                var response = (HttpServerResponse)null;
                foreach (var handler in _outputs)
                {
                    if (!handler.HandlesOutput(request, result))
                        continue;
                    response = await handler.ProcessOutput(request, result);
                    break;
                }
                foreach (var handler in _encoders)
                    response = await handler.Process(response);
                return response;
            }
            catch
            {
                return new HttpServerResponseBuilder().WithStatusCode(500).Build();
            }
        }

        public IHttpStagedHandlerBuilder Add(IHttpRequestDecoder component)
        {
            _decoders.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpRequestEnhancer component)
        {
            _enhancers.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpInputProcessor component)
        {
            _inputs.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpPreprocessor component)
        {
            _pre.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpProcessor component)
        {
            _processor = component;
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpPostprocessor component)
        {
            _post.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpOutputProcessor component)
        {
            _outputs.Add(component);
            return this;
        }

        public IHttpStagedHandlerBuilder Add(IHttpRequestEncoder component)
        {
            _encoders.Add(component);
            return this;
        }

        public IHttpRouteHandler Build()
        {
            return this;
        }
    }
}
