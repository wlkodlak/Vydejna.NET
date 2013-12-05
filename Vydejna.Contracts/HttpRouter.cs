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
    public interface IHttpRouter
    {
        HttpUsedRoute FindRoute(string url);
    }

    public class HttpRouter : IHttpRouter
    {
        private class RouteConfiguration
        {
            public ParametrizedUrl Url;
            public IHttpRouteHandler Handler;
        }
        private List<RouteConfiguration> _routes;
        public HttpRouter()
        {
            _routes = new List<RouteConfiguration>();
        }
        public HttpUsedRoute FindRoute(string url)
        {
            var urlParts = ParametrizedUrl.UrlForMatching(url);
            HttpUsedRoute bestRoute = null;
            int bestScore = 0;
            foreach (var item in _routes)
            {
                var match = item.Url.Match(urlParts);
                if (!match.Success)
                    continue;
                if (bestRoute == null || match.Score > bestScore)
                {
                    bestRoute = new HttpUsedRoute(item.Url, item.Handler).AddParameter(match);
                    bestScore = match.Score;
                }
            }
            return bestRoute;
        }
        public void AddRoute(string pattern, IHttpRouteHandler handler)
        {
            _routes.Add(new RouteConfiguration { Handler = handler, Url = new ParametrizedUrl(pattern) });
        }
    }

    public interface IHttpRouteHandler
    {
        Task<HttpServerResponse> Handle(HttpServerRequest request, IList<RequestParameter> routeParameters);
    }

    public class HttpUsedRoute
    {
        private readonly ParametrizedUrl _template;
        private readonly List<RequestParameter> _parameters;
        private readonly IHttpRouteHandler _handler;

        public ParametrizedUrl UrlTemplate { get { return _template; } }
        public IList<RequestParameter> RouteParameters { get { return _parameters; } }
        public IHttpRouteHandler Handler { get { return _handler; } }

        public HttpUsedRoute(ParametrizedUrl template, IHttpRouteHandler handler)
        {
            _template = template;
            _handler = handler;
            _parameters = new List<RequestParameter>();
        }

        public HttpUsedRoute AddParameter(RequestParameter parameter)
        {
            _parameters.Add(parameter);
            return this;
        }
        public HttpUsedRoute AddParameter(IEnumerable<RequestParameter> parameters)
        {
            _parameters.AddRange(parameters);
            return this;
        }
    }
}
