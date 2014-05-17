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
            var ctx = new HttpServerStagedContext(_rawContext);
            Assert.AreEqual("GET", ctx.Method, "Method");
            Assert.AreEqual("http://localhost/path/to/resource?id=448&name=wlkodlak", ctx.Url, "Method");
            Assert.AreEqual("127.0.0.1", ctx.ClientAddress, "Method");
        }

        [TestMethod]
        public void CopiesHeadersFromRawContext()
        {
            _rawContext.InputHeaders.Add("Content-Type", "text/xml");
            _rawContext.InputHeaders.Add("Referer", "http://referring.host.cz/");
            var ctx = new HttpServerStagedContext(_rawContext);
            Assert.AreEqual("text/xml", ctx.InputHeaders.ContentType, "ContentType");
            Assert.AreEqual("http://referring.host.cz/", ctx.InputHeaders.Referer, "Referer");
        }

        [TestMethod]
        public void LoadParameters()
        {
            _rawContext.RouteParameters.Add(new RequestParameter(RequestParameterType.Path, "res", "resource"));
            var ctx = new HttpServerStagedContext(_rawContext);
            ctx.LoadParameters();
            var parameters = string.Join(", ", ctx.RawParameters.OrderBy(p => p.Name).Select(p => string.Format("{0}:{1}", p.Name, p.Value)));
            Assert.AreEqual("id:448, name:wlkodlak, res:resource", parameters);
        }

        [TestMethod]
        public void WritesStatusAndHeadersOnClose()
        {
            var ctx = new HttpServerStagedContext(_rawContext);
            ctx.StatusCode = 302;
            ctx.OutputHeaders.Location = "http://new.location.com/";

            Assert.AreEqual(302, _rawContext.StatusCode, "StatusCode");
            var headers = string.Join("\r\n", _rawContext.OutputHeaders.Select(h => string.Format("{0}: {1}", h.Key, h.Value)));
            Assert.AreEqual("Location: http://new.location.com/", headers, "Headers");
        }

        [TestMethod]
        public void WritesOutputString()
        {
            var ctx = new HttpServerStagedContext(_rawContext);
            ctx.StatusCode = 200;
            ctx.OutputString = "This is the result";

            var expectedOutput = Encoding.UTF8.GetBytes("This is the result");
            CollectionAssert.AreEqual(expectedOutput, _rawContext.GetRawOutput());
        }
    }
}
