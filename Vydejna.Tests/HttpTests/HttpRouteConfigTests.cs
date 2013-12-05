using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpRouteConfigTests
    {
        private HttpRouteConfig _cfg;

        private class TestRequest { }
        private class TestResponse { }

        [TestMethod, Ignore]
        public void HttpRouteConfig_Missing()
        {
            _cfg.Route("/direct").To(new Mock<IHttpRouteHandler>().Object);
            var common = _cfg.Common()
                .With(new Mock<IHttpRequestDecoder>().Object)
                .With(new Mock<IHttpRequestEncoder>().Object)
                .With(new Mock<IHttpRequestEnhancer>().Object)
                .With(new Mock<IHttpInputProcessor>().Object)
                .With(new Mock<IHttpOutputProcessor>().Object);
            var withSession = _cfg.Common()
                .Using(common)
                .With(new Mock<IHttpPreprocessor>().Object)
                .With(new Mock<IHttpPostprocessor>().Object);
            _cfg.Route("/whole").To(new Mock<IHttpProcessor>().Object).Using(withSession);
            _cfg.Route("/separated").Parametrized<TestRequest>(rq => new TestRequest()).To(tr => new TestResponse()).Using(withSession);
        }
    }
}
