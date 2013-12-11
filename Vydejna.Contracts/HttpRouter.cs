﻿using System;
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

    public interface IHttpAddRoute
    {
        void AddRoute(string pattern, IHttpRouteHandler handler, IEnumerable<string> overridePrefix = null);
    }

    public class HttpRouter : IHttpRouter, IHttpAddRoute
    {
        private class RoutePath
        {
            public string Name;
            public List<RouteConfiguration> Routes;
            public List<RoutePath> SubPaths;
            public RoutePath(string name, bool hasContents)
            {
                Name = name;
                if (hasContents)
                {
                    Routes = new List<RouteConfiguration>();
                    SubPaths = new List<RoutePath>();
                }
            }
        }
        private class RoutePathComparer : IComparer<RoutePath>
        {
            public int Compare(RoutePath x, RoutePath y)
            {
                if (ReferenceEquals(x, null))
                    return ReferenceEquals(y, null) ? 0 : -1;
                else
                    return string.CompareOrdinal(x.Name, y.Name);
            }
        }

        private class RouteConfiguration
        {
            public ParametrizedUrl Url;
            public IHttpRouteHandler Handler;
        }
        private RoutePath _paths;
        private List<RouteConfiguration> _routes;
        private RoutePathComparer _pathComparer;
        private UpdateLock _lock;

        public HttpRouter()
        {
            _routes = new List<RouteConfiguration>();
            _paths = new RoutePath(null, true);
            _pathComparer = new RoutePathComparer();
            _lock = new UpdateLock();
        }

        public HttpUsedRoute FindRoute(string url)
        {
            using (_lock.Read())
            {
                if (_routes.Count == 0)
                    return null;
                var urlParts = ParametrizedUrl.UrlForMatching(url);
                var candidates = FindCandidates(urlParts);
                return FindRouteFromCandidates(urlParts, candidates);
            }
        }

        private IEnumerable<RouteConfiguration> FindCandidates(ParametrizedUrlParts urlParts)
        {
            var folder = GetFolder(urlParts, false);
            if (folder == null)
                return new RouteConfiguration[0];
            else
                return folder.Routes;
        }

        private static HttpUsedRoute FindRouteFromCandidates(ParametrizedUrlParts urlParts, IEnumerable<RouteConfiguration> candidates)
        {
            HttpUsedRoute bestRoute = null;
            int bestScore = 0;
            foreach (var item in candidates)
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

        public void AddRoute(string pattern, IHttpRouteHandler handler, IEnumerable<string> overridePrefix = null)
        {
            using (_lock.Update())
            {
                var route = new RouteConfiguration { Handler = handler, Url = new ParametrizedUrl(pattern) };
                _lock.Write();
                _routes.Add(route);
                AddPrefix(route.Url.Prefix, route);
                if (overridePrefix != null)
                {
                    foreach (var prefixString in overridePrefix)
                    {
                        var overridenPath = new ParametrizedUrl(prefixString);
                        AddPrefix(overridenPath.Prefix, route);
                    }
                }
            }
        }

        private void AddPrefix(ParametrizedUrlParts prefix, RouteConfiguration route)
        {
            var folder = GetFolder(prefix, true);
            folder.Routes.Add(route);
        }

        private RoutePath GetFolder(ParametrizedUrlParts prefix, bool createIfMissing)
        {
            var folder = _paths;
            var searched = new RoutePath(null, false);
            var maxLevel = prefix.Count;
            for (var level = 0; level < maxLevel; level++)
            {
                var subFolder = (RoutePath)null;
                searched.Name = prefix[level];
                var index = folder.SubPaths.BinarySearch(searched, _pathComparer);
                if (index >= 0)
                    subFolder = folder.SubPaths[index];
                else if (createIfMissing)
                {
                    subFolder = new RoutePath(prefix[level], true);
                    folder.SubPaths.Insert(~index, subFolder);
                }
                else
                    return folder;
                folder = subFolder;
            }
            return folder;
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
