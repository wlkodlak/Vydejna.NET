using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpServerDispatcherTests
    {
        private HttpServerDispatcher _disp;
        private HttpServerRequest _request;
        private TestRouter _router;
        private TestHandler _handler;

        [TestInitialize]
        public void Initialize()
        {
            _router = new TestRouter();
            _handler = new TestHandler();
            _disp = new HttpServerDispatcher(_router);
            _request = new HttpServerRequest();
        }

        private class TestRouter : IHttpRouter
        {
            public string RoutedUrl;
            public HttpUsedRoute UsedRoute;
            public HttpUsedRoute FindRoute(string url)
            {
                RoutedUrl = url;
                return UsedRoute;
            }
        }
        private class TestHandler : IHttpRouteHandler
        {
            public HttpServerRequest Request;
            public IList<RequestParameter> RouteParameters;
            public HttpServerResponse Response;

            public Task<HttpServerResponse> Handle(HttpServerRequest request, IList<RequestParameter> routeParameters)
            {
                Request = request;
                RouteParameters = routeParameters;
                return TaskResult.GetCompletedTask(Response);
            }
        }

        [TestMethod]
        public void DispatchGet()
        {
            _request.Url = "http://localhost/article/4952";
            _request.Method = "GET";
            _request.Headers.AcceptTypes = new[] { "*/*" };
            _router.UsedRoute = 
                new HttpUsedRoute(new ParametrizedUrl("/article/{id}"), _handler)
                .AddParameter(new RequestParameter(RequestParameterType.Path, "id", "4952"));
            _handler.Response = new HttpServerResponseBuilder().Redirect("http://localhost/oops").Build();
            
            var dispatcherResponse = _disp.ProcessRequest(_request).GetAwaiter().GetResult();
            
            Assert.AreEqual("http://localhost/article/4952", _router.RoutedUrl, "RoutedUrl");
            Assert.AreSame(_request, _handler.Request, "Request");
            Assert.AreSame(_router.UsedRoute.RouteParameters, _handler.RouteParameters, "RouteParameters");
            Assert.AreSame(_handler.Response, dispatcherResponse, "Response");
        }
    }
}
