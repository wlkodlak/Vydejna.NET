using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpSerializationTests
    {
        [TestMethod]
        public void PickJsonSerializer()
        {
            var context = new TestStagedContext();
            context.InputHeaders.AcceptTypes.Add("application/json");
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickSerializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerJson));
        }

        [TestMethod]
        public void PickDefaultSerializer()
        {
            var context = new TestStagedContext();
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickSerializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerJson));
        }

        [TestMethod]
        public void PickXmlSerializer()
        {
            var context = new TestStagedContext();
            context.InputHeaders.AcceptTypes.Add("text/xml");
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickSerializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerXml));
        }

        [TestMethod]
        public void PickNextSerializer()
        {
            var textSerializer = new Mock<IHttpSerializer>();
            var context = new TestStagedContext();
            context.InputHeaders.AcceptTypes.Add("application/unknown");
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            next.Setup(x => x.PickSerializer(context, options, null)).Returns(textSerializer.Object);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickSerializer(context, options, next.Object);
            Assert.AreSame(textSerializer.Object, serializer);
        }

        [TestMethod]
        public void PickJsonDeserializer()
        {
            var context = new TestStagedContext();
            context.InputHeaders.ContentType = "application/json";
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickDeserializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerJson));
        }

        [TestMethod]
        public void PickXmlDeserializer()
        {
            var context = new TestStagedContext();
            context.InputHeaders.ContentType = "text/xml";
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickDeserializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerXml));
        }

        [TestMethod]
        public void PickJsonDeserializerWithoutSpecifiedContentType()
        {
            var context = new TestStagedContext();
            context.InputString = "{ \"name\": \"value\" }";
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickDeserializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerJson));
        }

        [TestMethod]
        public void PickXmlDeserializerWithoutSpecifiedContentType()
        {
            var context = new TestStagedContext();
            context.InputString = "<root/>";
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickDeserializer(context, options, next.Object);
            Assert.IsInstanceOfType(serializer, typeof(HttpSerializerXml));
        }

        [TestMethod]
        public void PickNextDeserializer()
        {
            var textSerializer = new Mock<IHttpSerializer>();
            var context = new TestStagedContext();
            context.InputHeaders.ContentType = "application/unknown";
            var options = new IHttpSerializer[] { new HttpSerializerJson(), new HttpSerializerXml() };
            var next = new Mock<ISerializerPicker>(MockBehavior.Strict);
            next.Setup(x => x.PickDeserializer(context, options, null)).Returns(textSerializer.Object);
            var picker = new HttpSerializerPicker();
            var serializer = picker.PickDeserializer(context, options, next.Object);
            Assert.AreSame(textSerializer.Object, serializer);
        }

        private class TestStagedContext : IHttpServerStagedContext
        {
            public string Method { get; set; }
            public string Url { get; set; }
            public string ClientAddress { get; set; }
            public string InputString { get; set; }
            public int StatusCode { get; set; }
            public string OutputString { get; set; }
            public IHttpSerializer InputSerializer { get; set; }
            public IHttpSerializer OutputSerializer { get; set; }
            public IHttpServerStagedHeaders InputHeaders { get; set; }
            public IHttpServerStagedHeaders OutputHeaders { get; set; }
            public IEnumerable<RequestParameter> RawParameters { get; set; }
            public IHttpProcessedParameter Parameter(string name) { return null; }
            public IHttpProcessedParameter PostData(string name) { return null; }
            public IHttpProcessedParameter Route(string name) { return null; }
            public void Close() { }
            public TestStagedContext()
            {
                InputHeaders = new HttpServerStagedContextHeaders();
                OutputHeaders = new HttpServerStagedContextHeaders();
            }
        }

    }
}
