using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ServiceStack.Text;

namespace Vydejna.Contracts
{
    public class HttpRouter : IHttpServerDispatcher
    {
        private HttpRouteConfigurator _cfg;

        public HttpRouter(HttpRouteConfigurator cfg)
        {
            _cfg = cfg;
        }

        public async Task<HttpServerResponse> ProcessRequest(HttpServerRequest request)
        {
            try
            {
                var route = _cfg.FindRoute(request.Url);
                foreach(var queryParameter in ParametrizedUrl.ParseQueryString(request.Url))
                    request.AddParameter(queryParameter);
                foreach (var routeParameter in route.RouteParameters)
                    request.AddParameter(routeParameter);
                if (route.IsDirectHandler)
                    return await route.ProcessRequestDirectly(request);
                else
                {
                    string contentType = request.Headers.ContentType;
                    foreach (var processor in route.InputProcessors)
                    {
                        if (processor.HandlesContentType(contentType))
                        {
                            request.PostDataObject = await processor.ProcessInput(request);
                            break;
                        }
                    }
                    object result;
                    try
                    {
                        result = await route.ProcessRequest(request);
                    }
                    catch (Exception ex)
                    {
                        result = ex;
                    }
                    foreach (var processor in route.OutputProcessors)
                    {
                        if (processor.HandlesOutput(request, result))
                        {
                            return await processor.ProcessOutput(request, result);
                        }
                    }
                    return new HttpServerResponseBuilder()
                        .WithStatusCode(System.Net.HttpStatusCode.NoContent)
                        .Build();
                }
            }
            catch (Exception)
            {
                return new HttpServerResponseBuilder()
                    .WithStatusCode(System.Net.HttpStatusCode.InternalServerError)
                    .Build();
            }
        }
    }

    public interface IHttpHandler
    {
        Task<HttpServerResponse> ProcessRequest(HttpServerRequest request);
    }

    public interface IHttpUsedRoute
    {
        ParametrizedUrl UrlPattern { get; }
        bool IsDirectHandler { get; }
        Task<HttpServerResponse> ProcessRequestDirectly(HttpServerRequest request);
        IEnumerable<RequestParameter> RouteParameters { get; }
        IEnumerable<IHttpInputProcessor> InputProcessors { get; }
        Task<object> ProcessRequest(HttpServerRequest request);
        IEnumerable<IHttpOutputProcessor> OutputProcessors { get; }
    }

    public interface IHttpInputProcessor
    {
        bool HandlesContentType(string contentType);
        Task<object> ProcessInput(HttpServerRequest request);
    }

    public interface IHttpOutputProcessor
    {
        bool HandlesOutput(HttpServerRequest request, object response);
        Task<HttpServerResponse> ProcessOutput(HttpServerRequest request, object response);
    }

    public interface IFindRoute
    {
        IHttpUsedRoute FindRoute(string url);
    }

    public class HttpRouteConfigurator : IFindRoute
    {
        private List<RoutePattern> _routes;

        public HttpRouteConfigurator()
        {
            _routes = new List<RoutePattern>();
        }

        public IHttpUsedRoute FindRoute(string url)
        {
            var urlParts = ParametrizedUrl.UrlForMatching(url);
            return _routes
                .Select(r => new UsedRoute(r, r.UrlPattern.Match(urlParts)))
                .Where(r => r.IsMatch).OrderBy(r => r.Score).FirstOrDefault();
        }

        private class UsedRoute : IHttpUsedRoute
        {
            private ParametrizedUrlMatch _match;
            private ParametrizedUrl _urlPattern;
            private bool _isDirectHandler;
            private List<IHttpInputProcessor> _inputProcessors;
            private List<IHttpOutputProcessor> _outputProcessors;
            private IRouteHandler _processingHandler;
            private IHttpHandler _directHandler;

            public UsedRoute(RoutePattern pattern, ParametrizedUrlMatch match)
            {
                _urlPattern = pattern.UrlPattern;
                _isDirectHandler = pattern.IsDirectHandler;
                _inputProcessors = pattern.InputProcessors.Reverse().ToList();
                _outputProcessors = pattern.OutputProcessors.Reverse().ToList();
                _processingHandler = pattern.ProcessingHandler;
                _directHandler = pattern.DirectHandler;
                _match = match;
            }

            public bool IsMatch { get { return _match.Success; } }
            public int Score { get { return _match.Score; } }

            public ParametrizedUrl UrlPattern
            {
                get { return _urlPattern; }
            }

            public bool IsDirectHandler
            {
                get { return _isDirectHandler; }
            }

            public Task<HttpServerResponse> ProcessRequestDirectly(HttpServerRequest request)
            {
                return _directHandler.ProcessRequest(request);
            }

            public IEnumerable<RequestParameter> RouteParameters
            {
                get { return _match; }
            }

            public IEnumerable<IHttpInputProcessor> InputProcessors
            {
                get { return _inputProcessors; }
            }

            public Task<object> ProcessRequest(HttpServerRequest request)
            {
                return _processingHandler.ProcessRequest(request);
            }

            public IEnumerable<IHttpOutputProcessor> OutputProcessors
            {
                get { return _outputProcessors; }
            }
        }


        public IHttpRouteCfg Route(string pattern)
        {
            return new RoutePattern(new ParametrizedUrl(pattern), this);
        }

        private void AddRoute(RoutePattern routePattern)
        {
            _routes.Add(routePattern);
        }

        private interface IRouteHandler
        {
            Task<object> ProcessRequest(HttpServerRequest request);
        }

        private class RouteHandlerSimple : IRouteHandler
        {
            private Func<HttpServerRequest, Task<object>> handler;
            public RouteHandlerSimple(Func<HttpServerRequest, Task<object>> handler)
            {
                this.handler = handler;
            }
            public Task<object> ProcessRequest(HttpServerRequest request)
            {
                return handler(request);
            }
        }

        private class RouteHandlerPreprocessed<TResult> : IRouteHandler
        {
            private Func<HttpServerRequest, Task<TResult>> preprocessor;
            private Func<TResult, Task<object>> handler;

            public RouteHandlerPreprocessed(Func<HttpServerRequest, Task<TResult>> preprocessor, Func<TResult, Task<object>> handler)
            {
                this.preprocessor = preprocessor;
                this.handler = handler;
            }

            public async Task<object> ProcessRequest(HttpServerRequest request)
            {
                var preprocessed = await preprocessor(request);
                var result = await handler(preprocessed);
                return result;
            }
        }

        private class RouteBuilder<TResult> : IHttpRouteCfgThrough<TResult>
        {
            private RoutePattern _pattern;
            private Func<HttpServerRequest, Task<TResult>> preprocessor;

            public RouteBuilder(RoutePattern _pattern, Func<HttpServerRequest, Task<TResult>> preprocessor)
            {
                this._pattern = _pattern;
                this.preprocessor = preprocessor;
            }

            public IHttpRouteCfgTo To(Func<TResult, Task<object>> handler)
            {
                return _pattern.To(new RouteHandlerPreprocessed<TResult>(preprocessor, handler));
            }
        }

        private class RoutePattern : IHttpRouteCfg, IHttpRouteCfgTo
        {
            private List<IHttpInputProcessor> _inputProcessors;
            private List<IHttpOutputProcessor> _outputProcessors;

            public RoutePattern(ParametrizedUrl pattern, HttpRouteConfigurator configurator)
            {
                UrlPattern = pattern;
                Configurator = configurator;
                _inputProcessors = new List<IHttpInputProcessor>();
                _outputProcessors = new List<IHttpOutputProcessor>();
            }

            public IRouteHandler ProcessingHandler { get; set; }
            public IHttpHandler DirectHandler { get; set; }
            public ParametrizedUrl UrlPattern { get; private set; }
            public bool IsDirectHandler { get; private set; }
            public IEnumerable<IHttpInputProcessor> InputProcessors { get { return _inputProcessors; } }
            public IEnumerable<IHttpOutputProcessor> OutputProcessors { get { return _outputProcessors; } }
            public HttpRouteConfigurator Configurator { get; private set; }

            public IHttpRouteCfgThrough<TResult> Through<TResult>(Func<HttpServerRequest, Task<TResult>> preprocessor)
            {
                return new RouteBuilder<TResult>(this, preprocessor);
            }
            public IHttpRouteCfgTo To(Func<HttpServerRequest, Task<object>> handler)
            {
                return To(new RouteHandlerSimple(handler));
            }
            public IHttpRouteCfgTo To(IRouteHandler handler)
            {
                ProcessingHandler = handler;
                Configurator.AddRoute(this);
                return this;
            }
            public void To(IHttpHandler handler)
            {
                DirectHandler = handler;
                IsDirectHandler = true;
                Configurator.AddRoute(this);
            }

            public IHttpRouteCfgTo OverrideFilters()
            {
                _inputProcessors.Clear();
                _outputProcessors.Clear();
                return this;
            }
            public IHttpRouteCfgTo Using(IHttpInputProcessor resolver)
            {
                _inputProcessors.Add(resolver);
                return this;
            }
            public IHttpRouteCfgTo Using(IHttpOutputProcessor processor)
            {
                _outputProcessors.Add(processor);
                return this;
            }
        }
    }

    public interface IHttpRouteCfg
    {
        IHttpRouteCfgThrough<TResult> Through<TResult>(Func<HttpServerRequest, Task<TResult>> preprocessor);
        IHttpRouteCfgTo To(Func<HttpServerRequest, Task<object>> handler);
        void To(IHttpHandler handler);
    }

    public interface IHttpRouteCfgThrough<TResult>
    {
        IHttpRouteCfgTo To(Func<TResult, Task<object>> handler);
    }

    public interface IHttpRouteCfgTo
    {
        IHttpRouteCfgTo OverrideFilters();
        IHttpRouteCfgTo Using(IHttpInputProcessor resolver);
        IHttpRouteCfgTo Using(IHttpOutputProcessor processor);
    }
}
