using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpServerHeadersTests
    {
        private HttpServerHeaders _headers;

        [TestInitialize]
        public void Initialize()
        {
            _headers = new HttpServerHeaders();
        }

        [TestMethod]
        public void Add_ValidContentType()
        {
            _headers.Add("Content-Type", "text/plain");
            Assert.AreEqual("text/plain", _headers.ContentType);
        }

        [TestMethod]
        public void Add_ValidContentLength()
        {
            _headers.Add("Content-Length", "5472");
            Assert.AreEqual(5472, _headers.ContentLength);
        }

        [TestMethod]
        public void Add_ValidContentLanguage()
        {
            _headers.Add("Content-Language", "en");
            Assert.AreEqual("en", _headers.ContentLanguage);
        }

        [TestMethod]
        public void Add_ValidReferer()
        {
            _headers.Add("Referer", "http://referer.com/");
            Assert.AreEqual("http://referer.com/", _headers.Referer);
        }

        [TestMethod]
        public void Add_ValidLocation()
        {
            _headers.Add("Location", "http://redirect.com/");
            Assert.AreEqual("http://redirect.com/", _headers.Location);
        }

        [TestMethod]
        public void Add_ValidAccept()
        {
            _headers.Add("Accept", "text/html, text/xml; q=0.9, application/json; q=0.8");
            var expected = new[] { "text/html", "text/xml", "application/json" };
            Assert.AreEqual(
                string.Join(", ", expected),
                string.Join(", ", _headers.AcceptTypes));
        }

        [TestMethod]
        public void Add_ValidAcceptLanguages()
        {
            _headers.Add("Accept-Language", "cs, en; q=0.9");
            var expected = new[] { "cs", "en" };
            Assert.AreEqual(
                string.Join(", ", expected),
                string.Join(", ", _headers.AcceptLanguages));
        }

        [TestMethod]
        public void Add_CustomHeader()
        {
            _headers.Add("X-UnknownHeader", "Value");
        }

        [TestMethod]
        public void EnumerateHeaders()
        {
            _headers.Add("Accept", "text/html, text/xml; q=0.9, application/json; q=0.8");
            _headers.Add("Accept-Language", "cs, en; q=0.9");
            _headers.ContentLanguage = "en";
            _headers.ContentLength = 571;
            _headers.ContentType = "application/json";
            _headers.Location = "http://new.location.com/";
            _headers.Referer = "https://referer.net/";
            _headers.Add("X-Custom", "customValue");

            var result = _headers.OrderBy(h => h.Name).Select(h => string.Format("{0}: {1}", h.Name, h.Value)).ToList();
            var expected = new[] { 
                "Accept: text/html, text/xml; q=0.9, application/json; q=0.8",
                "Accept-Language: cs, en; q=0.9",
                "Content-Language: en",
                "Content-Length: 571",
                "Content-Type: application/json",
                "Location: http://new.location.com/",
                "Referer: https://referer.net/",
                "X-Custom: customValue"
            };
            if (expected.Length != result.Count)
                Assert.AreEqual(string.Join(Environment.NewLine, expected), string.Join(Environment.NewLine, result), "Different counts");
            else for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], result[i]);
        }

        [TestMethod]
        public void GenerateAcceptHeaders()
        {
            _headers.AcceptLanguages = new[] { "cs", "en", "de" };
            _headers.AcceptTypes = new[] { "text/html", "application/xml" };

            var result = _headers.OrderBy(h => h.Name).Select(h => string.Format("{0}: {1}", h.Name, h.Value)).ToList();

            var expected = new[] { 
                "Accept: text/html, application/xml",
                "Accept-Language: cs, en, de"
            };
            Assert.AreEqual(string.Join(Environment.NewLine, expected), string.Join(Environment.NewLine, result));
        }

    }
}
