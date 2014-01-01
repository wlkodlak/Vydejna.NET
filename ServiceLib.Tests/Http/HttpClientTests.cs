using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Xml.Linq;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpClientTests
    {
        private static string _serverPrefix;
        private static TestServer _testServer;

        private class TestServer : IDisposable
        {
            private HttpListener _listener;
            private CancellationTokenSource _cancel;
            private Task _listenerTask;
            private string _serverPrefix;

            public TestServer(string prefix)
            {
                _serverPrefix = prefix;
                _listener = new HttpListener();
                _listener.Prefixes.Add(_serverPrefix);
            }

            public void Start()
            {
                _listener.Start();
                _cancel = new CancellationTokenSource();
                _listenerTask = Task.Factory.StartNew(TestServerWorker);
            }

            public void Dispose()
            {
                _cancel.Cancel();
                _cancel.Dispose();
                _listener.Close();
                _listenerTask.Wait();
            }

            private void TestServerWorker()
            {
                while (!_cancel.IsCancellationRequested)
                {
                    try
                    {
                        var ctx = _listener.GetContext();
                        var path = ctx.Request.Url.AbsolutePath;
                        if (path == "/articles/all")
                            HttpClientTests.GetRequestOk_Server(ctx.Request, ctx.Response);
                        else if (path == "/articles/fail")
                            HttpClientTests.GetRequestFail_Server(ctx.Request, ctx.Response);
                        else if (path == "/articles/post")
                            HttpClientTests.GetRequestPost_Server(ctx.Request, ctx.Response);
                        else
                            ctx.Response.StatusCode = 500;
                        ctx.Response.Close();
                    }
                    catch
                    {

                    }
                }
            }

        }

        [ClassInitialize]
        public static void InitializeFixture(TestContext ctx)
        {
            _serverPrefix = string.Format("http://localhost:{0}/", new Random().Next(49152, 65535));
            _testServer = new TestServer(_serverPrefix);
            _testServer.Start();
        }

        [ClassCleanup]
        public static void DisposeFixture()
        {
            _testServer.Dispose();
        }

        private static HttpClientResponse ExecuteHttpClient(HttpClientRequest request)
        {
            var mre = new ManualResetEventSlim();
            HttpClientResponse response = null;
            Exception exception = null;
            new HttpClient().Execute(request, resp => { response = resp; mre.Set(); }, ex => { exception = ex; mre.Set(); });
            mre.Wait(500);
            if (exception != null)
                throw exception.PreserveStackTrace();
            return response;
        }

        [TestMethod]
        public void GetRequestOk()
        {
            var request = new HttpClientRequest();
            request.Method = "GET";
            request.Url = _serverPrefix + "articles/all";
            request.Headers.Add(new HttpClientHeader("Accept", "text/html, application/xml, */*"));

            var response = ExecuteHttpClient(request);

            Assert.IsNotNull(response, "Response");
            var headers = response.Headers.ToLookup(h => h.Name, h => h.Value);
            var body = Encoding.UTF8.GetString(response.Body);
            var expectedBody = "<articles><accept>text/html</accept><accept>application/xml</accept><accept>*/*</accept></articles>";
            Assert.AreEqual(200, response.StatusCode, "Status code");
            Assert.AreEqual("application/json", headers["Content-Type"].FirstOrDefault());
            Assert.AreEqual(expectedBody, body, "Body");
        }

        private static void GetRequestOk_Server(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.ContentType = "application/json";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write("<articles>");
                foreach (var acceptedType in request.AcceptTypes)
                    writer.Write("<accept>{0}</accept>", acceptedType);
                writer.Write("</articles>");
            }
        }

        [TestMethod]
        public void GetRequestFail()
        {
            var request = new HttpClientRequest();
            request.Method = "GET";
            request.Url = _serverPrefix + "articles/fail";
            request.Headers.Add(new HttpClientHeader("Accept", "text/html, application/xml, */*"));

            var response = ExecuteHttpClient(request);

            Assert.IsNotNull(response, "Response");
            Assert.AreEqual(403, response.StatusCode, "Status code");
        }

        private static void GetRequestFail_Server(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.StatusCode = 403;
        }

        [TestMethod]
        public void GetRequestPost()
        {
            var requestXml = new XElement("Root", new XElement("Name", "Milan Wilczak"));
            var request = new HttpClientRequest();
            request.Method = "POST";
            request.Url = _serverPrefix + "articles/post";
            request.Headers.Add(new HttpClientHeader("Content-Type", "application/xml"));
            request.Body = Encoding.UTF8.GetBytes(requestXml.ToString());

            var response = ExecuteHttpClient(request);

            Assert.IsNotNull(response, "Response");
            Assert.AreEqual(200, response.StatusCode, "Status code");
            var headers = response.Headers.ToLookup(h => h.Name, h => h.Value);
            Assert.AreEqual("application/xml", headers["Content-Type"].FirstOrDefault());
            var responseString = Encoding.UTF8.GetString(response.Body);
            var responseXml = XElement.Parse(responseString);
            Assert.AreEqual(requestXml.ToString(), responseXml.ToString());
        }

        private static void GetRequestPost_Server(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.ContentType = request.ContentType;
            using (var reader = new StreamReader(request.InputStream))
            using (var writer = new StreamWriter(response.OutputStream))
                writer.Write(reader.ReadToEnd());
        }

    }
}
