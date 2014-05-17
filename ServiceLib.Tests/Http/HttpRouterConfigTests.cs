using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpRouterConfigTests
    {
        private IHttpRouteCommonConfigurator _cfg;
        private TestRouter _router;
        private TestRawHandler _rawHandler;
        private IHttpRouteCommonConfigurator _basic;
        private IHttpRouteCommonConfigurator _articles;
        private IHttpRouteCommonConfigurator _categories;

        [TestInitialize]
        public void SetupSubRouter()
        {
            _router = new TestRouter();
            _cfg = new HttpRouterCommon(_router);

            _rawHandler = new TestRawHandler();
            _cfg.Route("raw").To(_rawHandler);

            _basic = _cfg.SubRouter()
                .WithSerializer(new HttpSerializerJson())
                .WithSerializer(new HttpSerializerXml())
                .WithPicker(new HttpSerializerPicker());
            _articles = _basic.SubRouter().ForFolder("articles");
            _articles.Route("").To(new TestProcessor("listarticles"));
            _articles.Route("{id}").To(new TestProcessor("articles"));
            _categories = _basic.SubRouter().ForFolder("categories");
            _categories.Route("").To(new TestProcessor("listcategories"));
            _categories.Route("{name}").To(new TestProcessor("category"));
            _cfg.Commit();
        }

        [TestMethod]
        public void AllPatternsAreRegistered()
        {
            var expected = "/articles, /articles/{id}, /categories, /categories/{name}, /raw";
            var actual = string.Join(", ", _router.Routes.Keys.OrderBy(p => p));
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void RawIsMappedToRawHandler()
        {
            Assert.AreSame(_rawHandler, _router.Routes["/raw"]);
        }

        [TestMethod]
        public void BasicRouterHasPropertiesSetManually()
        {
            var extended = (IHttpRouteCommonConfiguratorExtended)_basic;
            Assert.AreEqual("", extended.GetPath());
            Assert.IsInstanceOfType(extended.GetPicker(), typeof(HttpSerializerPicker));
            var serializers = string.Join(", ", extended.GetSerializers().Select(s => s.GetType().Name).OrderBy(n => n));
            Assert.AreEqual("HttpSerializerJson, HttpSerializerXml", serializers);
        }

        [TestMethod]
        public void ArticlesRouterInheritsProperties()
        {
            var extended = (IHttpRouteCommonConfiguratorExtended)_articles;
            Assert.AreEqual("/articles", extended.GetPath());
            Assert.IsInstanceOfType(extended.GetPicker(), typeof(HttpSerializerPicker));
            var serializers = string.Join(", ", extended.GetSerializers().Select(s => s.GetType().Name).OrderBy(n => n));
            Assert.AreEqual("HttpSerializerJson, HttpSerializerXml", serializers);
        }

        [TestMethod]
        public void ProcessorBasedRoutesCreateStagedHandler()
        {
            Assert.IsInstanceOfType(_router.Routes["/articles"], typeof(HttpRouteStagedHandler));
        }

        private class TestRouter : IHttpAddRoute
        {
            public Dictionary<string, IHttpRouteHandler> Routes = new Dictionary<string, IHttpRouteHandler>();

            public void AddRoute(string pattern, IHttpRouteHandler handler, System.Collections.Generic.IEnumerable<string> overridePrefix = null)
            {
                Routes.Add(pattern, handler);
            }
        }

        private class TestProcessor : IHttpProcessor
        {
            public string Name;
            public TestProcessor(string name) { Name = name; }
            public Task Process(IHttpServerStagedContext context)
            {
                return TaskUtils.CompletedTask();
            }
        }

        private class TestRawHandler : IHttpRouteHandler
        {
            public Task Handle(IHttpServerRawContext context)
            {
                return TaskUtils.CompletedTask();
            }
        }
    }
}
