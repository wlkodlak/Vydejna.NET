using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Moq;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpRouterTests
    {
        private HttpRouter _cfg;

        private class TestRouteHandler : IHttpRouteHandler
        {
            public string Name;
            public TestRouteHandler(string name)
            {
                Name = name;
            }

            public Task<HttpServerResponse> Handle(HttpServerRequest request, IList<RequestParameter> routeParameters)
            {
                throw new NotImplementedException();
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            _cfg = new HttpRouter();
        }

        [TestMethod]
        public void EmptyRouter()
        {
            var route = _cfg.FindRoute("http://localhost/");
            Assert.IsNull(route);
        }

        [TestMethod]
        public void SingleRouteWithoutParameters()
        {
            var handler = new TestRouteHandler("default");
            _cfg.AddRoute("/default", handler);
            var route = _cfg.FindRoute("http://localhost/default");
            Assert.IsNotNull(route, "Was found");
            Assert.AreSame(handler, route.Handler, "Handler");
            Assert.AreEqual("/default", route.UrlTemplate.ToString(), "UrlTemplate");
            Assert.IsNotNull(route.RouteParameters, "RouteParameters");
            Assert.AreEqual(0, route.RouteParameters.Count, "RouteParameters.Count");
        }

        [TestMethod]
        public void SingleRouteWithParameters()
        {
            var handler = new TestRouteHandler("withparams");
            _cfg.AddRoute("/archive/{year}/{month}", handler);
            var route = _cfg.FindRoute("http://localhost/archive/2013/08");
            Assert.IsNotNull(route, "Was found");
            Assert.AreSame(handler, route.Handler, "Handler");
            Assert.AreEqual("/archive/{year}/{month}", route.UrlTemplate.ToString(), "UrlTemplate");
            Assert.IsNotNull(route.RouteParameters, "RouteParameters");
            Assert.AreEqual(2, route.RouteParameters.Count, "RouteParameters.Count");
            Assert.AreEqual(new RequestParameter(RequestParameterType.Path, "year", "2013"), route.RouteParameters[0], "RouteParameters[0]");
            Assert.AreEqual(new RequestParameter(RequestParameterType.Path, "month", "08"), route.RouteParameters[1], "RouteParameters[1]");
        }

        [TestMethod]
        public void MultipleDistinctRoutes()
        {
            var archive = new TestRouteHandler("archive");
            var categories = new TestRouteHandler("categories");
            var index = new TestRouteHandler("index");
            _cfg.AddRoute("/", index);
            _cfg.AddRoute("/category/{category}", categories);
            _cfg.AddRoute("/archive/{year}/{month}", archive);
            Assert.AreEqual("index", (_cfg.FindRoute("http://localhost/").Handler as TestRouteHandler).Name);
            Assert.AreEqual("categories", (_cfg.FindRoute("http://localhost/category/test").Handler as TestRouteHandler).Name);
            Assert.AreEqual("archive", (_cfg.FindRoute("http://localhost/archive/2012/04").Handler as TestRouteHandler).Name);
        }

        [TestMethod]
        public void MultipleConflictingRoutes()
        {
            var all = new TestRouteHandler("all");
            var simple = new TestRouteHandler("simple");
            var composite = new TestRouteHandler("composite");
            _cfg.AddRoute("/articles/all", all);
            _cfg.AddRoute("/articles/{type}", simple);
            _cfg.AddRoute("/articles/{year}-{month}", composite);
            Assert.AreEqual("all", (_cfg.FindRoute("http://localhost/articles/all").Handler as TestRouteHandler).Name);
            Assert.AreEqual("simple", (_cfg.FindRoute("http://localhost/articles/favorite").Handler as TestRouteHandler).Name);
            Assert.AreEqual("composite", (_cfg.FindRoute("http://localhost/articles/2012-04").Handler as TestRouteHandler).Name);
        }
    }
}
