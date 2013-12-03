using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpOutputDirectTests
    {
        private HttpOutputDirect _output;
        private HttpServerRequest _request;

        private class UnsupportedClass { }

        [TestInitialize]
        public void Initialize()
        {
            _output = new HttpOutputDirect();
            _request = new HttpServerRequest();
        }

        [TestMethod]
        public void UnsupportedType()
        {
            Assert.IsFalse(_output.HandlesOutput(_request, new UnsupportedClass()));
        }

        [TestMethod]
        public void HandlesOutput_HttpServerResponse()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new HttpServerResponseBuilder().WithRawBody(bodyBytes).Build();
            Assert.IsTrue(_output.HandlesOutput(_request, objectResponse));
        }
        [TestMethod]
        public void ProcessOutput_HttpServerResponse()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new HttpServerResponseBuilder().WithStatusCode(222).WithHeader("X-Value", "Value").WithRawBody(bodyBytes).Build();
            var finalResponse = _output.ProcessOutput(_request, objectResponse).GetAwaiter().GetResult();
            Assert.AreSame(objectResponse, finalResponse);
        }

        [TestMethod]
        public void HandlesOutput_HttpServerResponseBuilder()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new HttpServerResponseBuilder().WithRawBody(bodyBytes);
            Assert.IsTrue(_output.HandlesOutput(_request, objectResponse));
        }
        [TestMethod]
        public void ProcessOutput_HttpServerResponseBuilder()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new HttpServerResponseBuilder().WithStatusCode(222).WithHeader("X-Value", "Value").WithRawBody(bodyBytes);
            var finalResponse = _output.ProcessOutput(_request, objectResponse).GetAwaiter().GetResult();
            Assert.AreEqual(222, finalResponse.StatusCode, "StatusCode");
            Assert.AreEqual("Value", finalResponse.Headers.Where(h => h.Name == "X-Value").Select(h => h.Value).FirstOrDefault(), "Header X-Value");
            Assert.AreEqual(bodyString, Encoding.UTF8.GetString(finalResponse.RawBody), "RawBody");
        }

        [TestMethod]
        public void HandlesOutput_Stream()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new MemoryStream(bodyBytes);
            Assert.IsTrue(_output.HandlesOutput(_request, objectResponse));
        }
        [TestMethod]
        public void ProcessOutput_Stream()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var objectResponse = new MemoryStream(bodyBytes);
            var finalResponse = _output.ProcessOutput(_request, objectResponse).GetAwaiter().GetResult();
            Assert.AreEqual(200, finalResponse.StatusCode, "StatusCode");
            Assert.AreEqual("application/octet-stream", finalResponse.Headers.ContentType, "ContentType");
            if (finalResponse.RawBody != null)
                Assert.AreEqual(bodyString, Encoding.UTF8.GetString(finalResponse.RawBody), "RawBody");
            else if (finalResponse.StreamBody != null)
                Assert.AreSame(objectResponse, finalResponse.StreamBody, "StreamBody");
            else
                Assert.Fail("No response data");
        }

        [TestMethod]
        public void HandlesOutput_Bytes()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            Assert.IsTrue(_output.HandlesOutput(_request, bodyBytes));
        }
        [TestMethod]
        public void ProcessOutput_Bytes()
        {
            var bodyString = "Output string";
            var bodyBytes = Encoding.UTF8.GetBytes(bodyString);
            var finalResponse = _output.ProcessOutput(_request, bodyBytes).GetAwaiter().GetResult();
            Assert.AreEqual(200, finalResponse.StatusCode, "StatusCode");
            Assert.AreEqual("application/octet-stream", finalResponse.Headers.ContentType, "ContentType");
            if (finalResponse.RawBody != null)
                Assert.AreEqual(bodyBytes, finalResponse.RawBody, "RawBody");
            else if (finalResponse.StreamBody != null)
                Assert.AreSame(bodyBytes, new BinaryReader(finalResponse.StreamBody).ReadBytes(int.MaxValue), "StreamBody");
            else
                Assert.Fail("No response data");
        }

        [TestMethod]
        public void HandlesOutput_String()
        {
            var bodyString = "Output string";
            Assert.IsTrue(_output.HandlesOutput(_request, bodyString));
        }
        [TestMethod]
        public void ProcessOutput_String()
        {
            var bodyString = "Output string";
            var finalResponse = _output.ProcessOutput(_request, bodyString).GetAwaiter().GetResult();
            Assert.AreEqual(200, finalResponse.StatusCode, "StatusCode");
            Assert.AreEqual("text/plain", finalResponse.Headers.ContentType, "ContentType");
            if (finalResponse.RawBody != null)
                Assert.AreEqual(bodyString, Encoding.UTF8.GetString(finalResponse.RawBody), "RawBody");
            else if (finalResponse.StreamBody != null)
                Assert.AreEqual(bodyString, new StreamReader(finalResponse.StreamBody).ReadToEnd(), "StreamBody");
            else
                Assert.Fail("No response data");
        }

        [TestMethod]
        public void HandlesOutput_StringBuilder()
        {
            var bodyString = "Output string";
            var objectResponse = new StringBuilder().Append(bodyString);
            Assert.IsTrue(_output.HandlesOutput(_request, objectResponse));
        }
        [TestMethod]
        public void ProcessOutput_StringBuilder()
        {
            var bodyString = "Output string";
            var objectResponse = new StringBuilder().Append(bodyString);
            var finalResponse = _output.ProcessOutput(_request, objectResponse).GetAwaiter().GetResult();
            Assert.AreEqual(200, finalResponse.StatusCode, "StatusCode");
            Assert.AreEqual("text/plain", finalResponse.Headers.ContentType, "ContentType");
            if (finalResponse.RawBody != null)
                Assert.AreEqual(bodyString, Encoding.UTF8.GetString(finalResponse.RawBody), "RawBody");
            else if (finalResponse.StreamBody != null)
                Assert.AreEqual(bodyString, new StreamReader(finalResponse.StreamBody).ReadToEnd(), "StreamBody");
            else
                Assert.Fail("No response data");
        }
    }
}
