using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System.Linq;
using System.Text;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpServerStagedContextTests
    {
        private TestRawContext _rawContext;

        [TestInitialize]
        public void Initialize()
        {
            _rawContext = new TestRawContext("GET", "http://localhost/path/to/resource?id=448&name=wlkodlak", "127.0.0.1");
        }

        [TestMethod]
        public void CreateFromRawContext()
        {
            var ctx = new HttpServerStagedContext(_rawContext, new RequestParameter[0]);
            Assert.AreEqual("GET", ctx.Method, "Method");
            Assert.AreEqual("http://localhost/path/to/resource?id=448&name=wlkodlak", ctx.Url, "Method");
            Assert.AreEqual("127.0.0.1", ctx.ClientAddress, "Method");
        }

        [TestMethod]
        public void CopiesHeadersFromRawContext()
        {
            _rawContext.InputHeaders.Add("Content-Type", "text/xml");
            _rawContext.InputHeaders.Add("Referer", "http://referring.host.cz/");
            var ctx = new HttpServerStagedContext(_rawContext, new RequestParameter[0]);
            Assert.AreEqual("text/xml", ctx.InputHeaders.ContentType, "ContentType");
            Assert.AreEqual("http://referring.host.cz/", ctx.InputHeaders.Referer, "Referer");
        }

        [TestMethod]
        public void LoadParameters()
        {
            var routeParameters = new[] { new RequestParameter(RequestParameterType.Path, "res", "resource") };
            var ctx = new HttpServerStagedContext(_rawContext, routeParameters);
            ctx.LoadParameters();
            var parameters = string.Join(", ", ctx.RawParameters.OrderBy(p => p.Name).Select(p => string.Format("{0}:{1}", p.Name, p.Value)));
            Assert.AreEqual("id:448, name:wlkodlak, res:resource", parameters);
        }
    }
}
