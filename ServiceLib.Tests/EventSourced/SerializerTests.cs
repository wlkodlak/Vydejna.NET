using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class SerializerTests
    {
        private TypeMapper _mapper;
        private EventSourcedJsonSerializer _serializer;
        [TestInitialize]
        public void Initialize()
        {
            _mapper = new TypeMapper();
            _mapper.Register<TestMessage>();
            _serializer = new EventSourcedJsonSerializer(_mapper);
        }

        [TestMethod]
        public void HandlesFormat()
        {
            Assert.IsTrue(_serializer.HandlesFormat("json"));
        }

        [TestMethod]
        public void Mapping()
        {
            Assert.AreEqual(typeof(TestMessage), _serializer.GetTypeFromName("ServiceLib.Tests.EventSourced.SerializerTests.TestMessage"));
            Assert.AreEqual("ServiceLib.Tests.EventSourced.SerializerTests.TestMessage", _serializer.GetTypeName(typeof(TestMessage)));
        }

        [TestMethod]
        public void SerializationRoundTrip()
        {
            var original = new TestMessage
            {
                IntValue = 4, 
                StringValue = "Hello",
                Element = new TestSubMessage
                {
                    Value = "World"
                }
            };
            var serialized = new EventStoreEvent();
            _serializer.Serialize(original, serialized);
            var copyObject = _serializer.Deserialize(serialized);
            Assert.IsNotNull(copyObject, "Deserialized");
            Assert.IsInstanceOfType(copyObject, typeof(TestMessage), "Deserialized");
            var copy = (TestMessage)copyObject;
            Assert.AreEqual(4, copy.IntValue, "IntValue");
            Assert.AreEqual("Hello", copy.StringValue, "StringValue");
            Assert.IsNotNull(copy.Element, "Element");
            Assert.AreEqual("World", copy.Element.Value, "Element.Value");
        }

        private class TestMessage
        {
            public int IntValue { get; set; }
            public string StringValue { get; set; }
            public TestSubMessage Element { get; set; }
        }
        private class TestSubMessage
        {
            public string Value { get; set; }
        }
    }
}
