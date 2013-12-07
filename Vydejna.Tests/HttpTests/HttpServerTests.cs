using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Vydejna.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpServerTests
    {
        private TestDispatcher _dispatcher;
        private HttpServer _server;
        private string _prefix;

        [TestInitialize]
        public void Initialize()
        {
            _prefix = string.Format("http://localhost:{0}/", new Random().Next(49152, 65535));
            _dispatcher = new TestDispatcher();
            _server = new HttpServer(new[] { _prefix }, _dispatcher);
            _server.SetupWorkerCount(1);
        }

        [TestCleanup]
        public void Cleanup()
        {
            ((IDisposable)_server).Dispose();
        }

        [TestMethod]
        public void CanStartAndStop()
        {
            _server.Start();
            _server.Stop();
        }

        [TestMethod]
        public void HandlePostRequest()
        {
            _dispatcher.Register("/article", HandleArticle);
            _server.Start();
            var response = SendHttpRequest("POST", "article?id=3345", "<xmldata>data</xmldata>");
            _server.Stop();
            Assert.AreEqual("<resp>OK</resp>", response);
        }

        private HttpServerResponse HandleArticle(HttpServerRequest request)
        {
            var post = ReadWholeStream(request.PostDataStream);
            Assert.AreEqual("<xmldata>data</xmldata>", post);
            return new HttpServerResponseBuilder()
                .WithStringBody("<resp>OK</resp>")
                .Build();
        }

        private string SendHttpRequest(string method, string url, string post)
        {
            var request = HttpWebRequest.CreateHttp(_prefix + url);
            request.Timeout = 10000;
            request.Method = method;
            if (method == "POST")
            {
                request.ContentType = "application/xml";
                using (var writer = new StreamWriter(request.GetRequestStream()))
                    writer.Write(post);
            }
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = (HttpWebResponse)ex.Response;
            }
            if (response == null)
                Assert.Fail("Request timed out");
            var result = ReadWholeStream(response.GetResponseStream());
            return result;
        }
        private string ReadWholeStream(Stream stream)
        {
            if (stream == null)
                return null;
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        private class TestDispatcher : IHttpServerDispatcher
        {
            private Dictionary<string, Func<HttpServerRequest, HttpServerResponse>> _routes;
            public TestDispatcher()
            {
                _routes = new Dictionary<string, Func<HttpServerRequest, HttpServerResponse>>();
            }
            public void Register(string url, Func<HttpServerRequest, HttpServerResponse> handler)
            {
                _routes[url] = handler;
            }
            public Task<HttpServerResponse> ProcessRequest(HttpServerRequest request)
            {
                var path = new Uri(request.Url).AbsolutePath;
                Func<HttpServerRequest, HttpServerResponse> handler;
                if (!_routes.TryGetValue(path, out handler))
                    return new HttpServerResponseBuilder().WithStatusCode(404).BuildTask();
                try
                {
                    return TaskResult.GetCompletedTask(handler(request));
                }
                catch (Exception ex)
                {
                    return new HttpServerResponseBuilder()
                        .WithStatusCode(500)
                        .WithStringBody(ex.ToString())
                        .BuildTask();
                }
            }
        }
    }
}
