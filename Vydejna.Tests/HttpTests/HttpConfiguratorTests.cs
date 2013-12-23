using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpConfiguratorTests
    {
        [TestMethod]
        public void RouteDirect()
        {
            var router = new TestRouter();
            var cfg = new HttpRouteConfig(router, "/");
            var processor = new Mock<IHttpRouteHandler>();

            cfg.Route("SeznamNaradi").To(processor.Object);
            cfg.Commit();

            Assert.AreEqual(1, router.Calls, "AddRoute calls");
            Assert.AreEqual("/SeznamNaradi", router.Pattern, "Pattern");
            Assert.AreSame(processor.Object, router.Handler, "Handler");
            Assert.IsNull(router.OverridePrefix, "Prefixes");
        }

        [TestMethod]
        public void RouteProcessor()
        {
            var router = new TestRouter();
            var cfg = new HttpRouteConfig(router, "/")
                .WithSerializer(new HttpSerializerJson())
                .WithSerializer(new HttpSerializerXml())
                .WithPicker(new HttpSerializerPicker());
            var processor = new TestProcessor();

            cfg.Route("SeznamNaradi").To(processor);
            cfg.Commit();

            Assert.AreEqual(1, router.Calls, "AddRoute calls");
            Assert.AreEqual("/SeznamNaradi", router.Pattern, "Pattern");
            Assert.IsNotNull(router.Handler, "Handler null");
            Assert.IsNull(router.OverridePrefix, "Prefixes");
        }

        private class TestRouter : IHttpAddRoute
        {
            public int Calls;
            public string Pattern;
            public IHttpRouteHandler Handler;
            public string[] OverridePrefix;

            public void AddRoute(string pattern, IHttpRouteHandler handler, IEnumerable<string> overridePrefix = null)
            {
                Calls++;
                Pattern = pattern;
                Handler = handler;
                OverridePrefix = overridePrefix != null ? overridePrefix.ToArray() : null;
            }
        }

        private class TestProcessor : IHttpProcessor
        {

        }
    }
}
