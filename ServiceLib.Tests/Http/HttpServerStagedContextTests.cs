using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpServerStagedContextTests
    {
        private TestContext _rawContext;

        [TestInitialize]
        public void Initialize()
        {
            _rawContext = new TestContext("GET", "http://localhost/path/to/resource?id=448&name=wlkodlak", "127.0.0.1");
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
            
            ctx.Close();
            
            _rawContext.WaitForClose();
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

            ctx.Close();

            _rawContext.WaitForClose();
            var expectedOutput = Encoding.UTF8.GetBytes("This is the result");
            CollectionAssert.AreEqual(expectedOutput, _rawContext.GetRawOutput());
        }

        /*
         * Create context from raw context - method, url, client IP
         * Copies headers from raw context
         * Load parameters from route and url
         * Close writes properties and output string to raw context
         */

        private class TestContext : IHttpServerRawContext
        {
            private string _method, _url, _clientIp;
            private MemoryStream _input, _output;
            private ManualResetEventSlim _mre;

            public TestContext(string method, string url, string clientIp)
            {
                _method = method;
                _url = url;
                _clientIp = clientIp;
                _input = new MemoryStream();
                _output = new MemoryStream();
                InputHeaders = new TestHeaders();
                OutputHeaders = new TestHeaders();
                RouteParameters = new List<RequestParameter>();
                _mre = new ManualResetEventSlim();
            }
            public string Method { get { return _method; } }
            public string Url { get { return _url; } }
            public string ClientAddress { get { return _clientIp; } }
            public int StatusCode { get; set; }
            public Stream InputStream { get { return _input; } }
            public Stream OutputStream { get { return _output; } }
            public IList<RequestParameter> RouteParameters { get; private set; }
            public IHttpServerRawHeaders InputHeaders { get; private set; }
            public IHttpServerRawHeaders OutputHeaders { get; private set; }
            public void Close()
            {
                _mre.Set();
            }
            public bool WaitForClose()
            {
                return _mre.Wait(100);
            }
            public byte[] GetRawOutput()
            {
                return _output.ToArray();
            }
        }

        private class TestHeaders : IHttpServerRawHeaders
        {
            private List<KeyValuePair<string, string>> _data = new List<KeyValuePair<string, string>>();
            public void Add(string name, string value) { _data.Add(new KeyValuePair<string, string>(name, value)); }
            public void Clear() { _data.Clear(); }
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() { return _data.GetEnumerator(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
    }
}
