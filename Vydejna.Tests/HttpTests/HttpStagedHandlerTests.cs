using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Vydejna.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq.Expressions;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpStagedHandlerTests
    {
        private MockRepository _repo;
        private Mock<IHttpProcessor> _processor;
        private Mock<IHttpOutputProcessor> _output;
        private HttpServerRequest _request;
        private List<RequestParameter> _routeParameters;
        private IHttpStagedHandlerBuilder _builder;
        private HttpServerResponse _response;

        [TestInitialize]
        public void Initialize()
        {
            _repo = new MockRepository(MockBehavior.Strict);
            _processor = _repo.Create<IHttpProcessor>();
            _output = _repo.Create<IHttpOutputProcessor>();
            _request = new HttpServerRequest();
            _routeParameters = new List<RequestParameter>();
            _builder = HttpStagedHandler.CreateBuilder();
        }

        private void SetupProcessor(Func<HttpServerRequest, object> func, bool verify)
        {
            var setupProcessor = _processor
                .Setup(o => o.Process(It.IsAny<HttpServerRequest>()))
                .Returns<HttpServerRequest>(rq =>
                {
                    try { return TaskResult.GetCompletedTask(func(rq)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<object>(ex); }
                });
            if (verify)
                setupProcessor.Verifiable();
            _builder.Add(_processor.Object);
        }

        private void SetupOutput(Mock<IHttpOutputProcessor> output, bool handles, Func<HttpServerRequest, object, HttpServerResponse> func, bool verify)
        {
            var setupHandles = output
                .Setup(o => o.HandlesOutput(It.IsAny<HttpServerRequest>(), It.IsAny<object>()))
                .Returns(handles);
            var setupProcess = output
                .Setup(o => o.ProcessOutput(It.IsAny<HttpServerRequest>(), It.IsAny<object>()))
                .Returns<HttpServerRequest, object>((rq, rs) =>
                {
                    try { return TaskResult.GetCompletedTask(func(rq, rs)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<HttpServerResponse>(ex); }
                });
            if (verify)
            {
                setupHandles.Verifiable();
                if (handles)
                    setupProcess.Verifiable();
            }
            _builder.Add(output.Object);
        }

        private void SetupInput(Mock<IHttpInputProcessor> input, string contentType, Func<HttpServerRequest, object> func, bool verifyHandles = false, bool verifyProcess = false)
        {
            var setupHandles = input
                .Setup(i => i.HandlesContentType(It.IsAny<string>()))
                .Returns<string>(ct => ct == contentType);
            if (verifyHandles)
                setupHandles.Verifiable();
            var setupProcess = input
                .Setup(i => i.ProcessInput(It.IsAny<HttpServerRequest>()))
                .Returns<HttpServerRequest>(rq =>
                {
                    try { return TaskResult.GetCompletedTask<object>(func(rq)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<object>(ex); }
                });
            if (verifyProcess)
                setupProcess.Verifiable();
            _builder.Add(input.Object);
        }

        private void SetupPreprocess(Mock<IHttpPreprocessor> mock, Func<HttpServerRequest, object> func, bool verify)
        {
            var setupPreprocess = mock
                .Setup(m => m.Process(It.IsAny<HttpServerRequest>()))
                .Returns<HttpServerRequest>(rq =>
                {
                    try { return TaskResult.GetCompletedTask<object>(func(rq)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<object>(ex); }
                });
            if (verify)
                setupPreprocess.Verifiable();
            _builder.Add(mock.Object);
        }

        private void SetupPostprocess(Mock<IHttpPostprocessor> mock, Func<HttpServerRequest, object, object> func, bool verify)
        {
            var setupPostprocess = mock
                .Setup(m => m.Process(It.IsAny<HttpServerRequest>(), It.IsAny<object>()))
                .Returns<HttpServerRequest, object>((rq, rs) =>
                {
                    try { return TaskResult.GetCompletedTask<object>(func(rq, rs)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<object>(ex); }
                });
            if (verify)
                setupPostprocess.Verifiable();
            _builder.Add(mock.Object);
        }

        private void SetupDecoder(Mock<IHttpRequestDecoder> mock, Func<HttpServerRequest, HttpServerRequest> func, bool verify)
        {
            var setupDecoder = mock
                .Setup(m => m.Process(It.IsAny<HttpServerRequest>()))
                .Returns<HttpServerRequest>(rq =>
                {
                    try { return TaskResult.GetCompletedTask<HttpServerRequest>(func(rq)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<HttpServerRequest>(ex); }
                });
            if (verify)
                setupDecoder.Verifiable();
            _builder.Add(mock.Object);
        }

        private void SetupEnhancer(Mock<IHttpRequestEnhancer> mock, Func<HttpServerRequest, IEnumerable<RequestParameter>, HttpServerRequest> func, bool verify)
        {
            var setupEnhancer = mock
                .Setup(m => m.Process(It.IsAny<HttpServerRequest>(), It.IsAny<IEnumerable<RequestParameter>>()))
                .Returns<HttpServerRequest, IEnumerable<RequestParameter>>((rq, rp) =>
                {
                    try { return TaskResult.GetCompletedTask<HttpServerRequest>(func(rq, rp)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<HttpServerRequest>(ex); }
                });
            if (verify)
                setupEnhancer.Verifiable();
            _builder.Add(mock.Object);
        }

        private void SetupEncoder(Mock<IHttpRequestEncoder> mock, Func<HttpServerResponse, HttpServerResponse> func, bool verify)
        {
            var setupEncoder = mock
                .Setup(m => m.Process(It.IsAny<HttpServerResponse>()))
                .Returns<HttpServerResponse>(rq =>
                {
                    try { return TaskResult.GetCompletedTask<HttpServerResponse>(func(rq)); }
                    catch (Exception ex) { return TaskResult.GetFailedTask<HttpServerResponse>(ex); }
                });
            if (verify)
                setupEncoder.Verifiable();
            _builder.Add(mock.Object);
        }


        private void HandleRequest()
        {
            _response = _builder.Build().Handle(_request, _routeParameters).GetAwaiter().GetResult();
        }

        private void ExpectResponse(string expected)
        {
            _repo.Verify();
            Assert.IsNotNull(_response, "Response");
            var responseString = Encoding.UTF8.GetString(_response.RawBody ?? new byte[0]);
            Assert.AreEqual(expected, responseString, "Contents");
        }

        private void ExpectResponseStatus(int status)
        {
            _repo.Verify();
            Assert.IsNotNull(_response, "Response");
            Assert.AreEqual(status, _response.StatusCode, "Status code");
        }

        private void SetupPostData(string contentType, string contents)
        {
            _request.Headers.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(contents);
            var stream = new System.IO.MemoryStream(bytes);
            _request.PostDataStream = stream;
        }

        private HttpServerResponse ToResponse(string text)
        {
            var body = Encoding.UTF8.GetBytes(text);
            return new HttpServerResponseBuilder().WithRawBody(body).Build();
        }

        private string FromPostdata(HttpServerRequest request)
        {
            return new System.IO.StreamReader(request.PostDataStream, Encoding.UTF8).ReadToEnd();
        }

        private HttpServerRequest RequestCloneAppend(HttpServerRequest request, string append)
        {
            var newReq = new HttpServerRequest();
            newReq.Url = request.Url + append;
            return newReq;
        }

        private HttpServerResponse ResponseCloneAppend(HttpServerResponse response, string append)
        {
            var newResp = new HttpServerResponse();
            var appendBytes = new UTF8Encoding(false).GetBytes(append);
            newResp.RawBody = response.RawBody.Concat(appendBytes).ToArray();
            return newResp;
        }

        [TestMethod]
        public void RunOnlyProcessorAndOutput()
        {
            _request.Url = "http://localhost/test";
            SetupProcessor(rq => rq.Url, true);
            SetupOutput(_output, true, (rq, rs) => new HttpServerResponseBuilder().WithRawBody(Encoding.UTF8.GetBytes(rs.ToString())).Build(), true);
            HandleRequest();
            ExpectResponse("http://localhost/test");
        }

        [TestMethod]
        public void OutputIsPickedUsingHandlesOutput()
        {
            SetupProcessor(rq => null, false);
            SetupOutput(_repo.Create<IHttpOutputProcessor>(), false, null, true);
            SetupOutput(_repo.Create<IHttpOutputProcessor>(), true, (rq, rs) => ToResponse("Hello"), true);
            HandleRequest();
            ExpectResponse("Hello");
        }

        [TestMethod]
        public void ProcessorException()
        {
            SetupProcessor(rq => { throw new InvalidOperationException("Testing exception"); }, false);
            SetupOutput(_output, true, (rq, rs) => ToResponse(((Exception)rs).Message), false);
            HandleRequest();
            ExpectResponse("Testing exception");
        }

        [TestMethod]
        public void ProcessInputIntoObject()
        {
            SetupPostData("text/plain", "Request input");
            SetupInput(_repo.Create<IHttpInputProcessor>(), "application/json", rq => "", true);
            SetupInput(_repo.Create<IHttpInputProcessor>(), "text/plain", rq => FromPostdata(rq), true, true);
            SetupProcessor(rq => rq.PostDataObject, false);
            SetupOutput(_output, true, (rq, rs) => ToResponse((rs ?? "").ToString()), false);
            HandleRequest();
            ExpectResponse("Request input");
        }

        [TestMethod]
        public void PreAndPostProcessing()
        {
            SetupPreprocess(_repo.Create<IHttpPreprocessor>(), rq => "Session data", true);
            SetupProcessor(rq => rq.ContextObject, false);
            SetupOutput(_output, true, (rq, rs) => ToResponse((rs ?? "").ToString()), true);
            SetupPostprocess(_repo.Create<IHttpPostprocessor>(), (rq, rs) => (rs ?? "").ToString() + " saved", true);
            HandleRequest();
            ExpectResponse("Session data saved");
        }

        [TestMethod]
        public void SupportInChain()
        {
            _request.Url = "Base";
            SetupDecoder(_repo.Create<IHttpRequestDecoder>(), rq => RequestCloneAppend(rq, " DEC"), true);
            SetupEnhancer(_repo.Create<IHttpRequestEnhancer>(), (rq, rp) => RequestCloneAppend(rq, " ENHA"), true);
            SetupEnhancer(_repo.Create<IHttpRequestEnhancer>(), (rq, rp) => RequestCloneAppend(rq, " ENHB"), true);
            SetupProcessor(rq => rq, false);
            SetupOutput(_output, true, (rq, rs) => ToResponse(rq.Url), false);
            SetupEncoder(_repo.Create<IHttpRequestEncoder>(), rs => ResponseCloneAppend(rs, " ENC"), true);
            HandleRequest();
            ExpectResponse("Base DEC ENHA ENHB ENC");
        }

        [TestMethod]
        public void ErrorOutsideProcessor()
        {
            SetupProcessor(rq => "Normal result", false);
            SetupOutput(_output, true, (rq, rs) => { throw new InvalidOperationException("Exception"); }, false);
            HandleRequest();
            ExpectResponseStatus(500);
        }
    }
}
