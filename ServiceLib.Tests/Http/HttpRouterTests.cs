using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpRouterTests
    {
        private List<string> _output;
        private HttpRouter _router;

        [TestInitialize]
        public void Initialize()
        {
            _output = new List<string>();
            _router = new HttpRouter();
        }

        [TestMethod]
        public void Route()
        {
            AddRoute("/Articles/{id}", "ArticlesHandler");
            AddRoute("/Categories/all", "CategoriesAllHandler");
            AddRoute("/Categories/null", "CategoriesNullHandler");
            AddRoute("/Categories/{name}/{page}", "CategoriesPageHandler");
            AssertUsedRoute("/Articles/5584", "ArticlesHandler", "id: 5584");
            AssertUsedRoute("/Categories/all", "CategoriesAllHandler", "");
            AssertUsedRoute("/Categories/null", "CategoriesNullHandler", "");
            AssertUsedRoute("/Categories/favorite/2", "CategoriesPageHandler", "name: favorite, page: 2");
        }

        private void AddRoute(string pattern, string handlerName)
        {
            _router.AddRoute(pattern, new TestHandler(_output, handlerName));
        }

        private void AssertUsedRoute(string url, string handlerName, string parameters)
        {
            var route = _router.FindRoute("http://localhost" + url);
            _output.Clear();
            route.Handler.Handle(null);
            var actualHandler = _output.Count == 1 ? _output[0] : null;
            Assert.AreEqual(handlerName, actualHandler, "Handler for {0}", url);
            var actualParameters = string.Join(", ", route.RouteParameters.Select(p => string.Format("{0}: {1}", p.Name, p.Value)));
            Assert.AreEqual(parameters, actualParameters, "Parameters for {0}", url);
        }

        private class TestHandler : IHttpRouteHandler
        {
            private string _name;
            private List<string> _output;
            
            public TestHandler(List<string> output, string name)
            {
                _output = output;
                _name = name;
            }

            public Task Handle(IHttpServerRawContext context)
            {
                _output.Add(_name);
                return TaskUtils.CompletedTask();
            }
        }
    }
}
