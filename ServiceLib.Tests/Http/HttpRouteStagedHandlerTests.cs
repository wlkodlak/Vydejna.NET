using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using ServiceLib.Tests.TestUtils;
using System.Threading;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpRouteStagedHandlerTests
    {
        private Mock<IHttpSerializer> _serializer;
        private HttpRouteStagedHandler _handler;
        private TestRawContext _rawContext;
        private TestProcessor _processor;
        private IHttpServerStagedContext _staged;
        private Mock<ISerializerPicker> _picker;
        private IHttpSerializer[] _serializers;

        [TestInitialize]
        public void Initialize()
        {
            _picker = new Mock<ISerializerPicker>();
            _serializer = new Mock<IHttpSerializer>();
            _serializers = new[] { _serializer.Object, new Mock<IHttpSerializer>().Object };
            _processor = new TestProcessor();
            _handler = new HttpRouteStagedHandler(_picker.Object, _serializers, _processor);
        }

        [TestMethod]
        public void WithoutPostdata()
        {
            _rawContext = new TestRawContext("GET", "http://localhost/articles/483?showcomments=true", "10.3.24.22");
            _picker.Setup(p => p.PickDeserializer(It.IsAny<IHttpServerStagedContext>(), _serializers, null))
                .Returns(_serializer.Object).Verifiable();
            _picker.Setup(p => p.PickSerializer(It.IsAny<IHttpServerStagedContext>(), _serializers, null))
                .Returns(_serializer.Object).Verifiable();

            _handler.Handle(_rawContext);
            _staged = _processor.WaitForCall();

            Assert.IsNotNull(_staged, "Staged context not available");
            Assert.AreEqual(_rawContext.Method, _staged.Method, "Method");
            Assert.AreEqual(_rawContext.Url, _staged.Url, "Url");
            Assert.AreSame(_serializer.Object, _staged.OutputSerializer, "Serializer");
            Assert.AreEqual("", _staged.InputString, "InputString");
        }

        [TestMethod]
        public void WithPostdata()
        {
            _rawContext = new TestRawContext("POST", "http://localhost/articles/483/comments", "10.3.24.22");
            _picker.Setup(p => p.PickDeserializer(It.IsAny<IHttpServerStagedContext>(), _serializers, null))
                .Returns(_serializer.Object).Verifiable();
            _picker.Setup(p => p.PickSerializer(It.IsAny<IHttpServerStagedContext>(), _serializers, null))
                .Returns(_serializer.Object).Verifiable();
            _rawContext.SetInput(Encoding.UTF8.GetBytes("Hello World!"));

            _handler.Handle(_rawContext);
            _staged = _processor.WaitForCall();

            Assert.IsNotNull(_staged, "Staged context not available");
            Assert.AreEqual(_rawContext.Method, _staged.Method, "Method");
            Assert.AreEqual(_rawContext.Url, _staged.Url, "Url");
            Assert.AreSame(_serializer.Object, _staged.InputSerializer, "Serializer-Input");
            Assert.AreSame(_serializer.Object, _staged.OutputSerializer, "Serializer-Output");
            Assert.AreEqual("Hello World!", _staged.InputString, "InputString");
        }

        private class TestProcessor : IHttpProcessor
        {
            private ManualResetEventSlim _mre = new ManualResetEventSlim();
            private IHttpServerStagedContext _context;

            public IHttpServerStagedContext WaitForCall()
            {
                _mre.Wait(100);
                return _context;
            }

            public void Process(IHttpServerStagedContext context)
            {
                _context = context;
                _mre.Set();
            }
        }
    }
}
