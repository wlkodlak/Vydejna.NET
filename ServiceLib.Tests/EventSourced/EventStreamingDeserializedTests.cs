using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using ServiceLib.Tests.TestUtils;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStreamingDeserializedTests
    {
        private TestScheduler _scheduler;
        private TestStreaming _streaming;
        private TestStreamer _streamer;
        private TestSerializer _serializer;
        private EventStreamingDeserialized _deserialized;
        private Type[] _types;
        private Task<EventStreamingDeserializedEvent> _task;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _streamer = new TestStreamer();
            _streaming = new TestStreaming(_streamer);
            _serializer = new TestSerializer();
            _deserialized = new EventStreamingDeserialized(_streaming, _serializer);
            _types = new[] { typeof(TestEvent1), typeof(TestEvent2) };
            _task = null;
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
            _deserialized.Close();
            Assert.IsTrue(_streamer.Disposed);
        }

        [TestMethod]
        public void GetNextEventToStreamer()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            Assert.IsTrue(_streamer.Processing, "Processing");
            Assert.IsTrue(_streamer.Nowait, "Nowait");
        }

        [TestMethod]
        public void WhenEventIsNotAvailable()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            _streamer.SendEmptyEvent();
            ExpectEventNotAvailable();
        }

        [TestMethod]
        public void WhenSupportedEventArrives()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            _streamer.SendEvent("4496", "TestEvent1", "EventData");
            ExpectEvent("4496", "TestEvent1", "EventData");
        }

        [TestMethod]
        public void WhenUnsupportedEventArrives()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            _streamer.SendEvent("4496", "TestEvent3", "EventData");
            ExpectNoCall();
            Assert.IsTrue(_streamer.Processing, "Processing");
            Assert.IsTrue(_streamer.Nowait, "Nowait");
        }

        [TestMethod]
        public void WhenDeserializationFails()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            _streamer.SendEvent("4496", "TestEvent2", "FAIL");
            ExpectDeserializationError();
        }

        [TestMethod]
        public void WhenRetrievalFails()
        {
            _deserialized.Setup(new EventStoreToken("4493"), _types, "TestProcess");
            GetNextEvent(true);
            _streamer.SendError(new NotSupportedException("Sent error"));
            ExpectError();
        }

        private void GetNextEvent(bool nowait)
        {
            _task = _scheduler.Run(() => _deserialized.GetNextEvent(nowait), false);
        }

        private void ExpectEvent(string token, string type, string body)
        {
            _scheduler.Process();
            Assert.IsTrue(_task.IsCompleted, "Complete");
            Assert.IsNull(_task.Exception, "Exception");
            var evnt = _task.Result;
            Assert.IsNotNull(evnt, "Received result");
            Assert.IsNotNull(evnt.Event, "Received event");
            Assert.AreEqual(type, evnt.Event.GetType().Name, "Event type");
            Assert.AreEqual(body, (evnt.Event as TestEvent).Data, "Data");
        }
        private void ExpectEventNotAvailable()
        {
            _scheduler.Process();
            Assert.IsTrue(_task.IsCompleted, "Complete");
            Assert.IsNull(_task.Exception, "Exception");
            var evnt = _task.Result;
            Assert.IsNull(evnt, "Received result");
        }
        private void ExpectNoCall()
        {
            _scheduler.Process();
            Assert.IsFalse(_task.IsCompleted, "Complete");
        }
        private void ExpectError()
        {
            _scheduler.Process();
            Assert.IsTrue(_task.IsCompleted, "Complete");
            Assert.IsNotNull(_task.Exception, "Exception");
        }
        private void ExpectDeserializationError()
        {
            _scheduler.Process();
            Assert.IsTrue(_task.IsCompleted, "Complete");
            Assert.IsNull(_task.Exception, "Task exception");
            var evnt = _task.Result;
            Assert.IsNotNull(evnt, "Received result");
            Assert.IsNull(evnt.Event, "Received event");
            Assert.IsNotNull(evnt.Raw, "Received raw event");
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

            public object Deserialize(EventStoreSnapshot snapshot)
            {
                throw new NotSupportedException();
            }

            public void Serialize(object snapshot, EventStoreSnapshot stored)
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
            public string LastProcessName;
            public bool Disposed;
            public bool Processing;
            public bool Nowait;
            public TaskCompletionSource<EventStoreEvent> Task;

            public void Setup(EventStoreToken token, string processName)
            {
                LastProcessedToken = token;
                LastProcessName = processName;
                Disposed = false;
            }

            public Task<EventStoreEvent> GetNextEvent(bool withoutWaiting)
            {
                Assert.IsFalse(Disposed, "Disposed");
                Processing = true;
                Nowait = withoutWaiting;
                Task = new TaskCompletionSource<EventStoreEvent>();
                return Task.Task;
            }

            public Task MarkAsDeadLetter()
            {
                return TaskUtils.CompletedTask();
            }

            public void Dispose()
            {
                Disposed = true;
            }

            public void SendError(Exception exception)
            {
                Assert.IsTrue(Processing, "Streamer not active");
                Task.TrySetException(exception);
            }

            public void SendEvent(EventStoreEvent evnt)
            {
                Assert.IsTrue(Processing, "Streamer not active");
                if (evnt != null)
                    LastProcessedToken = evnt.Token;
                Task.TrySetResult(evnt);
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
