using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpStagedContextHeadersTests
    {
        [TestMethod]
        public void ConvertWeightedHeaderToComponents()
        {
            var hdr = new HttpServerStagedContextWeightedHeader("Accept");
            hdr.RawValue = "application/json; q=1.0, text/xml; q=0.9, text/plain, */*";
            Assert.AreEqual("application/json; q=1.0, text/xml; q=0.9, text/plain, */*", hdr.RawValue, "RawValue");
            Assert.IsTrue(hdr.IsSet, "IsSet");
            Assert.AreEqual(4, hdr.Count, "Count");
            var byIndex = string.Join(", ", Enumerable.Range(0, 4).Select(i => hdr[i]));
            var byEnumerator = string.Join(", ", hdr);
            var expected = "application/json, text/xml, text/plain, */*";
            Assert.AreEqual(expected, byIndex, "By index");
            Assert.AreEqual(expected, byEnumerator, "By enumerator");
        }

        [TestMethod]
        public void EmptyWeightedHeader()
        {
            var hdr = new HttpServerStagedContextWeightedHeader("Accept");
            Assert.IsFalse(hdr.IsSet, "IsSet");
            Assert.AreEqual(0, hdr.Count, "Count");
        }

        [TestMethod]
        public void CreateWeightedHeaderFromComponents()
        {
            var hdr = new HttpServerStagedContextWeightedHeader("Accept");
            hdr.Add("application/json");
            hdr.Add("text/xml");
            hdr.Add("*/*");
            Assert.IsTrue(hdr.IsSet, "IsSet");
            Assert.AreEqual(3, hdr.Count, "Count");
            Assert.AreEqual("application/json, text/xml, */*", hdr.RawValue, "RawValue");
        }

        [TestMethod]
        public void SetAndGetNormalHeaders()
        {
            var hdr = new HttpServerStagedContextHeaders();
            hdr.ContentLength = 588;
            hdr.ContentType = "text/plain";
            hdr.Referer = "http://referer/path";
            hdr.Location = "http://redirect.to/server/path";
            Assert.AreEqual(588, hdr.ContentLength, "ContentLength");
            Assert.AreEqual("text/plain", hdr.ContentType, "ContentType");
            Assert.AreEqual("http://referer/path", hdr.Referer, "Referer");
            Assert.AreEqual("http://redirect.to/server/path", hdr.Location, "Location");
        }

        [TestMethod]
        public void ParseRawHeaders()
        {
            var hdr = new HttpServerStagedContextHeaders();
            hdr.Add("Content-Length", "588");
            hdr.Add("Content-Type", "application/json");
            hdr.Add("Accept", "application/json, text/xml, */*");
            hdr.Add("Accept-Language", "en-us");
            hdr.Add("Referer", "http://referer/path");
            hdr.Add("X-Custom", "custom value");

            Assert.AreEqual(588, hdr.ContentLength);
            Assert.AreEqual("application/json", hdr.ContentType);
            Assert.AreEqual("application/json, text/xml, */*", string.Join(", ", hdr.AcceptTypes));
            Assert.AreEqual("http://referer/path", hdr.Referer, "Referer");
        }

        [TestMethod]
        public void GenerateRawHeaders()
        {
            var hdr = new HttpServerStagedContextHeaders();
            hdr.ContentLength = 588;
            hdr.ContentType = "text/plain";
            hdr.Location = "http://redirect.to/server/path";
            hdr.Add("X-Custom", "custom value");

            var expected = new[] { 
                "Content-Length: 588",
                "Content-Type: text/plain",
                "Location: http://redirect.to/server/path",
                "X-Custom: custom value"
            };
            var actual = hdr.OrderBy(h => h.Key).Select(h => string.Format("{0}: {1}", h.Key, h.Value)).ToList();
            Assert.AreEqual("\r\n" + string.Join("\r\n", expected) + "\r\n", "\r\n" + string.Join("\r\n", actual) + "\r\n");
        }
    }
}
