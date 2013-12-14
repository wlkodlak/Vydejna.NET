using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Tests.SystemTests
{
    [TestClass]
    public class HttpServerLoadTests : IDisposable
    {
        private TestDispatcher _dispatcher;
        private HttpServer _server;
        private string _prefix;

        [TestInitialize]
        public void Initialize()
        {
            _prefix = "http://localhost:61111/";
            _dispatcher = new TestDispatcher();
            _server = new HttpServer(new[] { _prefix }, _dispatcher);
            _server.SetupWorkerCount(32);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Dispose();
        }

        public void Dispose()
        {
            ((IDisposable)_server).Dispose();
        }

        //[TestMethod]
        public void RunServer()
        {
            var html = @"<html><head><title>Article</title><body><h1>Success</h1></body></html>";
            var articleResponse = new HttpServerResponseBuilder()
                .WithHeader("Content-Type", "text/html")
                .WithStringBody(html)
                .Build();
            var stopWait = new ManualResetEventSlim();
            _dispatcher.Register("/stopListener", rq =>
            {
                stopWait.Set();
                return new HttpServerResponseBuilder().WithStatusCode((int)HttpStatusCode.Accepted).Build();
            });
            _dispatcher.Register("/", rq => articleResponse);
            _server.Start();

            stopWait.Wait();
            _server.Stop();
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
