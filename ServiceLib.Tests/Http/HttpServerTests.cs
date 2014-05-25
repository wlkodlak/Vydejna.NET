using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpServerTests
    {
        private string _prefix;
        private HttpServer _server;
        private TestHttpDispatcher _dispatcher;
        private Predicate<ProcessState> _isWantedState;
        private ManualResetEventSlim _mre;

        [TestInitialize]
        public void Initialize()
        {
            _mre = new ManualResetEventSlim();
            _isWantedState = null;
            _prefix = string.Concat("http://localhost:", new Random().Next(50000, 60000), "/");
            _dispatcher = new TestHttpDispatcher(_prefix);
            _server = new HttpServer(new[] { _prefix }, _dispatcher);
            _server.Init(StateChanged, TaskScheduler.Default);
        }

        private void StateChanged(ProcessState state)
        {
            if (_isWantedState == null ||_isWantedState(state))
                _mre.Set();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_server.State != ProcessState.Inactive)
                PauseProcess(true);
        }

        [TestMethod]
        public void IncomingRequestHandledByDispatcher()
        {
            StartProcess();
            var request = (HttpWebRequest)WebRequest.Create(_prefix + "resource?param=55");
            var response = (HttpWebResponse)request.GetResponse();
            var responseString = GetResponseString(response);
            response.Close();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Status code");
            Assert.AreEqual("GET /resource?param=55", responseString, "Data");
        }

        [TestMethod]
        public void CanRedirect()
        {
            StartProcess();
            var request = (HttpWebRequest)WebRequest.Create(_prefix + "redirect");
            request.AllowAutoRedirect = false;
            var response = (HttpWebResponse)request.GetResponse();
            response.Close();
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode, "Status code");
            Assert.AreEqual("http://www.google.com/", response.Headers["Location"], "Location");
        }

        [TestMethod]
        public void IsRestartable()
        {
            StartProcess();
            PauseProcess();
            StartProcess();
            var request = (HttpWebRequest)WebRequest.Create(_prefix + "resource?param=55");
            var response = (HttpWebResponse)request.GetResponse();
            var responseString = GetResponseString(response);
            response.Close();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Status code");
            Assert.AreEqual("GET /resource?param=55", responseString, "Data");
        }

        /*
         * Returns response text
         * Can return redirect
         * Is restartable
         */

        private void StartProcess()
        {
            _isWantedState = ServerWasStarted;
            _mre.Reset();
            _server.Start();
            _mre.Wait();
            Assert.AreEqual(ProcessState.Running, _server.State, "Server state");
        }

        private void PauseProcess(bool isShutdown = false)
        {
            _isWantedState = s => s == ProcessState.Inactive;
            _mre.Reset();
            _server.Pause();
            _mre.Wait(500);
            if (!isShutdown)
                Assert.AreEqual(ProcessState.Inactive, _server.State, "Server state");
        }

        private bool ServerWasStarted(ProcessState state)
        {
            switch (state)
            {
                case ProcessState.Faulted:
                case ProcessState.Pausing:
                case ProcessState.Running:
                case ProcessState.Stopping:
                case ProcessState.Inactive:
                case ProcessState.Conflicted:
                    return true;
                default:
                    return false;

            }
        }

        private string GetResponseString(HttpWebResponse response)
        {
            var buffer = new byte[4096];
            var stream = response.GetResponseStream();
            var output = new MemoryStream();
            while (true)
            {
                var read = stream.Read(buffer, 0, 4096);
                if (read <= 0)
                    break;
                output.Write(buffer, 0, read);
            }
            var outputBytes = output.ToArray();
            return Encoding.UTF8.GetString(outputBytes);
        }

        private class TestHttpDispatcher : IHttpServerDispatcher
        {
            private string _prefix;

            public TestHttpDispatcher(string prefix)
            {
                _prefix = prefix;
            }

            public Task DispatchRequest(IHttpServerRawContext context)
            {
                if (context.Url.Contains("redirect"))
                {
                    context.StatusCode = (int)HttpStatusCode.Redirect;
                    context.OutputHeaders.Add("Location", "http://www.google.com/");
                    return TaskUtils.CompletedTask();
                }
                else
                {
                    context.StatusCode = 200;
                    var url = context.Url.StartsWith(_prefix) ? context.Url.Substring(_prefix.Length) : context.Url;
                    var response = string.Format("{0} /{1}", context.Method, url);
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    return Task.Factory.FromAsync(context.OutputStream.BeginWrite(responseBytes, 0, responseBytes.Length, null, null), context.OutputStream.EndWrite);
                }
            }
        }
    }
}
