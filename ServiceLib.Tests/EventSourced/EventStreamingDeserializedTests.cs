using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStreamingDeserializedTests
    {
        private TestStreaming _streaming;
        private TestStreamer _streamer;
        private TestSerializer _serializer;
        private EventStreamingDeserialized _deserialized;
        private Type[] _types;
        private string LastReceivedCall;
        private EventStoreToken LastReceivedToken;
        private object LastReceivedEvent;
        private Exception LastReceivedException;
        private EventStoreEvent LastReceivedRawEvent;

        [TestInitialize]
        public void Initialize()
        {
            _streamer = new TestStreamer();
            _streaming = new TestStreaming(_streamer);
            _serializer = new TestSerializer();
            _deserialized = new EventStreamingDeserialized(_streaming, _serializer);
            _types = new[] { typeof(TestEvent1), typeof(TestEvent2) };
            ClearLastReceived();
        }

        [TestMethod]
        public void SetupCreatesStreamer()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            Assert.AreEqual(new EventStoreToken("4493"), _streamer.LastProcessedToken);
        }

        [TestMethod]
        public void DisposeDisposesStreamer()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.Dispose();
            Assert.IsTrue(_streamer.Disposed);
        }

        [TestMethod]
        public void GetNextEventToStreamer()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent((t, e) => { }, () => { }, (x, e) => { }, true);
            Assert.IsTrue(_streamer.Processing, "Processing");
            Assert.IsTrue(_streamer.Nowait, "Nowait");
        }

        [TestMethod]
        public void WhenEventIsNotAvailable()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent(OnEventRead, OnEventNotAvailable, OnError, true);
            _streamer.SendEmptyEvent();
            ExpectEventNotAvailable();
        }

        [TestMethod]
        public void WhenSupportedEventArrives()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent(OnEventRead, OnEventNotAvailable, OnError, true);
            _streamer.SendEvent("4496", "TestEvent1", "EventData");
            ExpectEvent("4496", "TestEvent1", "EventData");
        }

        [TestMethod]
        public void WhenUnsupportedEventArrives()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent(OnEventRead, OnEventNotAvailable, OnError, true);
            _streamer.SendEvent("4496", "TestEvent3", "EventData");
            ExpectNoCall();
            Assert.IsTrue(_streamer.Processing, "Processing");
            Assert.IsTrue(_streamer.Nowait, "Nowait");
        }

        [TestMethod]
        public void WhenDeserializationFails()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent(OnEventRead, OnEventNotAvailable, OnError, true);
            _streamer.SendEvent("4496", "TestEvent2", "FAIL");
            ExpectError();
            Assert.IsNotNull(LastReceivedRawEvent, "Raw event");
        }

        [TestMethod]
        public void WhenRetrievalFails()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            _deserialized.GetNextEvent(OnEventRead, OnEventNotAvailable, OnError, true);
            _streamer.SendError(new NotSupportedException("Sent error"));
            ExpectError();
            Assert.IsNull(LastReceivedRawEvent, "Raw event");
        }

        private void ClearLastReceived()
        {
            LastReceivedCall = "none";
            LastReceivedEvent = null;
            LastReceivedToken = null;
            LastReceivedException = null;
            LastReceivedRawEvent = null;
        }
        private void OnEventRead(EventStoreToken token, object evnt)
        {
            LastReceivedCall = "success";
            LastReceivedToken = token;
            LastReceivedEvent = evnt;
        }
        private void OnEventNotAvailable()
        {
            LastReceivedCall = "empty";
        }
        private void OnError(Exception exception, EventStoreEvent evnt)
        {
            LastReceivedCall = "error";
            LastReceivedException = exception;
            LastReceivedRawEvent = evnt;
        }
        private void ExpectEvent(string token, string type, string body)
        {
            Assert.AreEqual("success", LastReceivedCall, "Call type");
            Assert.IsNotNull(LastReceivedEvent, "Received event");
            Assert.AreEqual(type, LastReceivedEvent.GetType().Name, "Event type");
            Assert.AreEqual(body, (LastReceivedEvent as TestEvent).Data, "Data");
        }
        private void ExpectEventNotAvailable()
        {
            Assert.AreEqual("empty", LastReceivedCall, "Call type");
        }
        private void ExpectNoCall()
        {
            Assert.AreEqual("none", LastReceivedCall, "Call type");
        }
        private void ExpectError()
        {
            Assert.AreEqual("error", LastReceivedCall, "Call type");
            Assert.IsNotNull(LastReceivedException, "Exception");
        }

        private class TestStreaming : IEventStreaming
        {
            private TestStreamer _streamer;

            public TestStreaming(TestStreamer streamer)
            {
                _streamer = streamer;
            }

            public IEventStreamer GetStreamer(EventStoreToken token, string processName)
            {
                _streamer.Setup(token, processName);
                return _streamer;
            }
        }

        private class TestSerializer : IEventSourcedSerializer
        {
            public bool HandlesFormat(string format)
            {
                return format == "text";
            }

            public object Deserialize(EventStoreEvent evt)
            {
                var objEvent = CreateEvent(evt.Type);
                objEvent.Data = evt.Body;
                if (evt.Body == "FAIL")
                    throw new NotSupportedException("FAIL event");
                return objEvent;
            }

            public void Serialize(object evt, EventStoreEvent stored)
            {
                throw new NotSupportedException();
            }

            public string GetTypeName(Type type)
            {
                return type.Name;
            }

            public Type GetTypeFromName(string typeName)
            {
                switch (typeName)
                {
                    case "TestEvent1": return typeof(TestEvent1);
                    case "TestEvent2": return typeof(TestEvent2);
                    case "TestEvent3": return typeof(TestEvent3);
                    default: return null;
                }
            }

            public TestEvent CreateEvent(string typeName)
            {
                switch (typeName)
                {
                    case "TestEvent1": return new TestEvent1();
                    case "TestEvent2": return new TestEvent2();
                    case "TestEvent3": return new TestEvent3();
                    default: throw new ArgumentOutOfRangeException(string.Format("Event type {0} is not supported"), typeName);
                }
            }
        }

        private class TestEvent
        {
            public string Data;
        }
        private class TestEvent1 : TestEvent { }
        private class TestEvent2 : TestEvent { }
        private class TestEvent3 : TestEvent { }

        private class TestStreamer : IEventStreamer
        {
            public EventStoreToken LastProcessedToken;
            public bool Disposed;
            public bool Processing;
            public bool Nowait;
            private Action<EventStoreEvent> _onComplete;
            private Action<Exception> _onError;
            private string LastProcessName;

            public void Setup(EventStoreToken token, string processName)
            {
                LastProcessedToken = token;
                LastProcessName = processName;
                Disposed = false;
            }

            public void GetNextEvent(Action<EventStoreEvent> onComplete, Action<Exception> onError, bool withoutWaiting)
            {
                Assert.IsFalse(Disposed, "Disposed");
                Processing = true;
                Nowait = withoutWaiting;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void MarkAsDeadLetter(Action onComplete, Action<Exception> onError)
            {

            }

            public void Dispose()
            {
                Disposed = true;
            }

            public void SendError(Exception exception)
            {
                Assert.IsTrue(Processing, "Streamer not active");
                _onError(exception);
            }

            public void SendEvent(EventStoreEvent evnt)
            {
                Assert.IsTrue(Processing, "Streamer not active");
                if (evnt != null)
                    LastProcessedToken = evnt.Token;
                _onComplete(evnt);
            }

            public void SendMessage(Message msg)
            {

            }

            public void SendEvent(string token, string type, string body)
            {
                var evnt = new EventStoreEvent();
                evnt.Format = "text";
                evnt.Body = body;
                evnt.StreamName = "stream";
                evnt.StreamVersion = 1;
                evnt.Token = new EventStoreToken(token);
                evnt.Type = type;
                SendEvent(evnt);
            }

            public void SendEmptyEvent()
            {
                SendEvent(null);
            }
        }
    }
}
