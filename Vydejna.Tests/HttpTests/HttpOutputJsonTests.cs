using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using ServiceStack.Text;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpOutputJsonTests
    {
        private class TestObject
        {
            public int IntData { get; set; }
            public string StringData { get; set; }
        }

        private HttpOutputJson<TestObject> _output;
        private HttpServerRequest _request;
        private TestObject _responseObject;

        [TestInitialize]
        public void Initialize()
        {
            _request = new HttpServerRequest();
            _request.Headers.AcceptTypes = new[] { "application/json" };
            _responseObject = new TestObject { IntData = 5, StringData = "Hello world" };
        }

        [TestMethod]
        public void HandlesOutput_DifferentType()
        {
            _output = new HttpOutputJson<TestObject>("DEFAULT");
            Assert.IsFalse(_output.HandlesOutput(_request, "Hello world"), "Should not handle string");
        }

        [TestMethod]
        public void HandlesOutput_Ignore_DifferentContentType()
        {
            _request.Headers.AcceptTypes = new[] { "application/xml" };
            _output = new HttpOutputJson<TestObject>("IGNORE");
            Assert.IsTrue(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void HandlesOutput_Default_EmptyContentType()
        {
            _request.Headers.AcceptTypes = null;
            _output = new HttpOutputJson<TestObject>("DEFAULT");
            Assert.IsTrue(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void HandlesOutput_Default_DifferentContentType()
        {
            _request.Headers.AcceptTypes = new[] { "application/xml" };
            _output = new HttpOutputJson<TestObject>("DEFAULT");
            Assert.IsFalse(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void HandlesOutput_Strict_EmptyContentType()
        {
            _request.Headers.AcceptTypes = null;
            _output = new HttpOutputJson<TestObject>("STRICT");
            Assert.IsFalse(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void HandlesOutput_Strict_DifferentContentType()
        {
            _request.Headers.AcceptTypes = new[] { "application/xml" };
            _output = new HttpOutputJson<TestObject>("STRICT");
            Assert.IsFalse(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void HandlesOutput_Strict_ContainsContentType()
        {
            _request.Headers.AcceptTypes = new[] { "application/xml", "application/json" };
            _output = new HttpOutputJson<TestObject>("STRICT");
            Assert.IsTrue(_output.HandlesOutput(_request, _responseObject));
        }

        [TestMethod]
        public void ProcessOutput()
        {
            _output = new HttpOutputJson<TestObject>();
            var response = _output.ProcessOutput(_request, _responseObject).GetAwaiter().GetResult();
            Assert.AreEqual(200, response.StatusCode, "StatusCode");
            Assert.AreEqual("application/json", response.Headers.ContentType, "ContentType");
            string json = null;
            if (response.StreamBody != null)
                json = new System.IO.StreamReader(response.StreamBody).ReadToEnd();
            else if (response.RawBody != null)
                json = Encoding.UTF8.GetString(response.RawBody);
            else
                Assert.Fail("No response data");
            var expectedJson = JsonSerializer.SerializeToString(_responseObject);
            Assert.AreEqual(expectedJson, json, "JSON");
        }
    }
}
