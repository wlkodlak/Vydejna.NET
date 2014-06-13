using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class RestClientTest
    {
        private class RestClientTestClass : RestClient
        {
            public RestClientTestClass(string url, IHttpClient client)
                : base(url, client, null)
            {

            }

            public override RestClient SetPayload<T>(T data)
            {
                SetRawPayload(Encoding.UTF8.GetBytes(data.ToString()));
                return this;
            }

            protected override RestClientResult CreateResult(HttpClientResponse response)
            {
                return new RestClientTestResult(response);
            }
        }

        private class RestClientTestResult : RestClientResult
        {
            public RestClientTestResult(HttpClientResponse response)
                : base(response)
            {

            }

            public override T GetPayload<T>()
            {
                var value = Encoding.UTF8.GetString(RawData);
                if (typeof(T) == typeof(string))
                    return (T)(object)value;
                else
                    return default(T);
            }
        }

        private RestClientTester _tester;

        [TestInitialize]
        public void Initialize()
        {
            _tester = new RestClientTester();
        }

        [TestMethod]
        public void SimpleGet()
        {
            var svc = new RestClientTestClass("http://localhost/resource/", _tester.HttpClient);
            _tester.ExpectedUrl = "http://localhost/resource/";
            _tester.PreparedResponse = new HttpClientResponseBuilder().WithStringPayload("OK").Build();
            var result = _tester.RunTest(svc);
            Assert.AreEqual("OK", result.GetPayload<string>());
        }

        [TestMethod]
        public void QueryStringParameters()
        {
            var svc = new RestClientTestClass("http://localhost/resource/", _tester.HttpClient);
            svc.AddParameter("parameter", "HelloWorld");
            svc.AddParameter("param2", "5847");
            _tester.ExpectedUrl = "http://localhost/resource/?parameter=HelloWorld&param2=5847";
            _tester.PreparedResponse = new HttpClientResponseBuilder().WithStringPayload("Response").Build();
            var result = _tester.RunTest(svc);
            Assert.AreEqual("Response", result.GetPayload<string>());
        }

        [TestMethod]
        public void PathParameters()
        {
            var svc = new RestClientTestClass("http://localhost/{controller}/fixed/{id}/", _tester.HttpClient);
            svc.AddPath("controller", "ResourceController");
            svc.AddPath("id", "58472");
            _tester.ExpectedUrl = "http://localhost/ResourceController/fixed/58472/";
            _tester.PreparedResponse = new HttpClientResponseBuilder().WithStringPayload("Response").Build();
            var result = _tester.RunTest(svc);
            Assert.AreEqual("Response", result.GetPayload<string>());
        }
    }

    public class RestClientTester
    {
        public IHttpClient HttpClient { get { return _httpClient; } }
        public HttpClientRequest Request { get { return _httpRequest; } }
        public string ExpectedMethod;
        public string ExpectedUrl;
        public bool? ExpectedResponse;
        public HttpClientResponse PreparedResponse;

        public RestClientTester()
        {
            _httpClient = new TestHttpClient();
        }

        private TestHttpClient _httpClient;
        private HttpClientRequest _httpRequest;

        private class TestHttpClient : IHttpClient
        {
            public HttpClientRequest LastRequest;
            private TaskCompletionSource<HttpClientResponse> _tcs;
            private ManualResetEventSlim _mre = new ManualResetEventSlim();

            public void SendResponse(HttpClientResponse response)
            {
                _tcs.TrySetResult(response);
            }

            public Task<HttpClientResponse> Execute(HttpClientRequest request)
            {
                LastRequest = request;
                _tcs = new TaskCompletionSource<HttpClientResponse>();
                _mre.Set();
                return _tcs.Task;
            }

            public bool WaitForExecute(int timeout = 100)
            {
                return _mre.Wait(timeout);
            }
        }

        public RestClientResult RunTest(RestClient restClient)
        {
            var scheduler = new TestScheduler();
            var task = scheduler.Run(() => restClient.Execute(), false);

            _httpClient.WaitForExecute();
            _httpClient.SendResponse(PreparedResponse);
            _httpRequest = _httpClient.LastRequest;

            scheduler.Process();
            Assert.IsTrue(task.IsCompleted, "Completed");
            var response = task.Result;
            if (ExpectedResponse != null)
            {
                if (ExpectedResponse.Value)
                    Assert.IsNotNull(_httpRequest, "No request sent");
                else
                    Assert.IsNull(_httpRequest, "No request expected");
            }
            if (ExpectedMethod != null)
                Assert.AreEqual(ExpectedMethod, _httpRequest.Method, "Method");
            if (ExpectedUrl != null)
                Assert.AreEqual(ExpectedUrl, _httpRequest.Url, "Url");
            return response;
        }
    }

    public class HttpClientResponseBuilder
    {
        private HttpClientResponse _response = new HttpClientResponse();

        public HttpClientResponseBuilder WithStringPayload(string payload)
        {
            _response.Body = Encoding.UTF8.GetBytes(payload);
            return this;
        }

        public HttpClientResponseBuilder WithHeader(string name, string value)
        {
            _response.Headers.Add(new HttpClientHeader(name, value));
            return this;
        }

        public HttpClientResponseBuilder WithStatus(int status)
        {
            _response.StatusCode = status;
            return this;
        }

        public HttpClientResponse Build()
        {
            return _response;
        }
    }
}
