using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using System.Globalization;
using System.Xml.Linq;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpServerRequestTests_Parameters
    {
        private HttpServerRequest _request;

        [TestInitialize]
        public void Initialize()
        {
            _request = new HttpServerRequest();
            _request.AddParameter(new RequestParameter(RequestParameterType.QueryString, "integer", "1742"));
            _request.AddParameter(new RequestParameter(RequestParameterType.QueryString, "string", "datel"));
            _request.AddParameter(new RequestParameter(RequestParameterType.QueryString, "empty", ""));
            _request.AddParameter(new RequestParameter(RequestParameterType.QueryString, "date", "2013-11-24"));
        }

        [TestMethod]
        public void MandatoryIntegerValid()
        {
            Assert.AreEqual(1742, _request.Parameter("integer").AsInteger().Mandatory());
        }

        [TestMethod, ExpectedException(typeof(RequestParameterMissingException))]
        public void MandatoryIntegerMissing()
        {
            _request.Parameter("missing").AsInteger().Mandatory();
        }

        [TestMethod, ExpectedException(typeof(RequestParameterInvalidException))]
        public void MandatoryIntegerInvalid()
        {
            _request.Parameter("string").AsInteger().Mandatory();
        }

        [TestMethod]
        public void OptionalIntegerValid()
        {
            Assert.AreEqual(1742, _request.Parameter("integer").AsInteger().Optional(1111));
        }

        [TestMethod, ExpectedException(typeof(RequestParameterInvalidException))]
        public void OptionalIntegerInvalid()
        {
            _request.Parameter("string").AsInteger().Mandatory();
        }

        [TestMethod]
        public void OptionalIntegerEmpty()
        {
            Assert.AreEqual(1111, _request.Parameter("empty").AsInteger().Optional(1111));
        }

        [TestMethod]
        public void OptionalDatetimeMissing()
        {
            var value = _request
                .Parameter("empty")
                .As<DateTime>(s => DateTime.ParseExact(s, "yyyy-M-d", CultureInfo.InvariantCulture))
                .Optional(DateTime.MaxValue);
            Assert.AreEqual(DateTime.MaxValue, value);
        }

        [TestMethod, ExpectedException(typeof(RequestParameterInvalidException))]
        public void OptionalDatetimeInvalid()
        {
            _request
                .Parameter("string")
                .As<DateTime>(s => DateTime.ParseExact(s, "yyyy-M-d", CultureInfo.InvariantCulture))
                .Optional(DateTime.MaxValue);
        }

        [TestMethod]
        public void OptionalDatetimeValid()
        {
            var value = _request
                .Parameter("date")
                .As<DateTime>(s => DateTime.ParseExact(s, "yyyy-M-d", CultureInfo.InvariantCulture))
                .Optional(DateTime.MaxValue);
            Assert.AreEqual(new DateTime(2013, 11, 24), value);
        }

        [TestMethod, ExpectedException(typeof(RequestParameterMissingException))]
        public void MandatoryStringEmpty()
        {
            _request.Parameter("empty").AsString().Mandatory();
        }

        [TestMethod]
        public void OptionalStringEmpty()
        {
            Assert.AreEqual("default", _request.Parameter("empty").AsString().Optional("default"));
        }

        [TestMethod]
        public void OptionalStringValid()
        {
            Assert.AreEqual("datel", _request.Parameter("string").AsString().Optional("default"));
        }
    }

    [TestClass]
    public class HttpServerRequestTests_Route
    {
        private HttpServerRequest _request;

        [TestInitialize]
        public void Initialize()
        {
            _request = new HttpServerRequest();
            _request.AddParameter(new RequestParameter(RequestParameterType.Path, "integer", "1147"));
            _request.AddParameter(new RequestParameter(RequestParameterType.Path, "string", "username"));
            _request.AddParameter(new RequestParameter(RequestParameterType.Path, "date", "2013-12-05"));
        }

        [TestMethod]
        public void ValidInteger()
        {
            Assert.AreEqual(1147, _request.Route("integer").AsInteger().Mandatory());
        }

        [TestMethod]
        public void ValidString()
        {
            Assert.AreEqual("username", _request.Route("string").AsString().Optional());
        }

        [TestMethod]
        public void ValidDate()
        {
            var value = _request.Route("date").As<DateTime>(s => DateTime.ParseExact(s, "yyyy-M-d", CultureInfo.InvariantCulture)).Optional(DateTime.MaxValue);
            Assert.AreEqual(new DateTime(2013, 12, 5), value);
        }
    }

    [TestClass]
    public class HttpServerRequestTests_Properties
    {
        [TestMethod]
        public void PropertiesExist()
        {
            var request = new HttpServerRequest();
            request.Method = "POST";
            request.PostDataObject = new XElement("PostElement");
            request.Url = "http://localhost:5547/path/to/resource?param=value";
            request.PostDataStream = new System.IO.MemoryStream();
            Assert.IsNotNull(request.Headers, "Headers");
            Assert.AreEqual("POST", request.Method, "Method");
            Assert.IsNotNull(request.PostDataObject, "PostDataObject");
            Assert.AreEqual("http://localhost:5547/path/to/resource?param=value", request.Url, "Url");
            Assert.IsNotNull(request.PostDataStream, "PostDataStream");
        }
    }
}
