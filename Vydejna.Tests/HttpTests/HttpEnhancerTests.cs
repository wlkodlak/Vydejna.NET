using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpEnhancerTests_Get
    {
        private IHttpRequestEnhancer _enhancer;
        private HttpServerRequest _request;
        private HttpServerRequest _result;
        private IList<RequestParameter> _route;

        [TestInitialize]
        public void Initialize()
        {
            _enhancer = new HttpUrlEnhancer();
        }

        [TestMethod]
        public void AddsQueryStringParameters()
        {
            SetupRequest("http://localhost/path/to/resource?id=445&param=true");
            ProcessRequest();
            VerifyParameter("id", "445");
            VerifyParameter("param", "true");
        }

        [TestMethod]
        public void AddsPathParameters()
        {
            SetupRequest("http://localhost/article/48732");
            SetupRoute("id", "48732");
            ProcessRequest();
            VerifyRoute("id", "48732");
        }

        private void SetupRequest(string url)
        {
            _request = new HttpServerRequest();
            _request.Url = url;
            _request.Method = "GET";
            _route = new List<RequestParameter>();
        }
        private void SetupRoute(string name, string value)
        {
            _route.Add(new RequestParameter(RequestParameterType.Path, name, value));
        }
        private void ProcessRequest()
        {
            _result = _enhancer.Process(_request, _route).GetAwaiter().GetResult();
        }
        private void VerifyParameter(string name, string value)
        {
            Assert.IsNotNull(_result, "Result NULL");
            var actual = _result.Parameter(name).AsString().Optional();
            Assert.AreEqual(value, actual, "Parameter {0}", name);
        }
        private void VerifyRoute(string name, string value)
        {
            Assert.IsNotNull(_result, "Result NULL");
            var actual = _result.Route(name).AsString().Optional();
            Assert.AreEqual(value, actual, "Parameter {0}", name);
        }
    }
}
