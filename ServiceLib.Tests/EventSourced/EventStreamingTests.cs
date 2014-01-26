using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStreamingTests
    {
        private TestExecutor _executor;
        private TestEventStore _store;
        private EventStreaming _streaming;
        private VirtualTime _time;
        private NetworkBusInMemory _messaging;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _store = new TestEventStore(_executor);
            _time = new VirtualTime();
            _messaging = new NetworkBusInMemory(_executor, _time);
            _streaming = new EventStreaming(_store, _executor, _messaging).BatchSize(5);
        }

        [TestMethod]
        public void FirstCallUsesInitialToken()
        {
            _store.AddToStream("aaa", new[] { 
                Create("Type1", "58472"),
                Create("Type2", "5475"),
                Create("Type2", "5475"),
                Create("Type2", "5475"),
                Create("Type2", "5475"),
                Create("Type2", "5475")
            });
            var streamer = _streaming.GetStreamer(new EventStoreToken("3"), "TestProcess");
            EventStoreEvent evnt = null;
            streamer.GetNextEvent(e => evnt = e, ThrowError, true);
            _executor.Process();
            Assert.IsNotNull(evnt, "Event null");
            Assert.AreEqual(new EventStoreToken("4"), evnt.Token, "Token");
            Assert.AreEqual("Get 5 from 3", _store.GetStreamingLog().FirstOrDefault(s => s.StartsWith("Get")), "Log");
        }

        [TestMethod]
        public void NextCallUsesLastRetrievedEventsToken()
        {
            _store.AddToStream("aaa", new[] { 
                Create("Type1", "58472"),
                Create("Type2", "5475"),
                Create("Type2", "5475"),
                Create("Type2", "00000"),
            });
            var streamer = _streaming.GetStreamer(new EventStoreToken("3"), "TestProcess");
            EventStoreEvent evnt = null;
            streamer.GetNextEvent(e => evnt = e, ThrowError, true);
            _executor.Process();
            _store.AddToStream("aaa", new[] { 
                Create("Type2", "5475"),
                Create("Type2", "5475"),
            });
            _store.ClearStreamingLog();

            streamer.GetNextEvent(e => evnt = e, ThrowError, true);
            _executor.Process();
            Assert.IsNotNull(evnt, "Event null");
            Assert.AreEqual(new EventStoreToken("5"), evnt.Token, "Token");
            Assert.AreEqual("Get 5 from 4", _store.GetStreamingLog().FirstOrDefault(s => s.StartsWith("Get")), "Log");
        }

        [TestMethod]
        public void ErrorWhenRetrieving()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            Exception exception = null;
            streamer.GetNextEvent(e => { }, ex => exception = ex, false);
            _executor.Process();
            _store.SendFailure();
            _executor.Process();
            Assert.IsNotNull(exception, "Exception caught");
        }

        [TestMethod]
        public void RetryingAfterErrorIsPossible()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            Exception exception = null;
            streamer.GetNextEvent(e => { }, ex => exception = ex, false);
            _executor.Process();
            _store.SendFailure();
            _executor.Process();

            EventStoreEvent evnt = null;
            _store.AddToStream("aaa", new[] { Create("Type1", "58472") });
            streamer.GetNextEvent(e => evnt = e, ThrowError, true);
            _executor.Process();

            Assert.IsNotNull(evnt, "Event received");
        }

        [TestMethod]
        public void NullEventOnNowaitWithoutAvailableEvents()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            EventStoreEvent evnt = Create("FAIL", "FAIL");
            streamer.GetNextEvent(e => evnt = e, ThrowError, true);
            _executor.Process();
            Assert.IsNull(evnt, "Received event");
        }

        [TestMethod]
        public void WaitingCallReturnsNullOnDispose()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            EventStoreEvent evnt = Create("FAIL", "FAIL");
            streamer.GetNextEvent(e => evnt = e, ThrowError, false);
            _executor.Process();
            streamer.Dispose();
            _executor.Process();
            Assert.IsNull(evnt, "Received event");
        }

        [TestMethod]
        public void WaitingCallCanHandleNullOnLongPoll()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            EventStoreEvent evnt = Create("FAIL", "FAIL");
            streamer.GetNextEvent(e => evnt = e, ThrowError, false);
            _executor.Process();
            _store.EndLongPoll();
            _executor.Process();
        }

        [TestMethod]
        public void PreloadedEventsAreUsedOnNextCalls()
        {
            var received = new List<EventStoreEvent>();
            _store.AddToStream("aaa", new[]
                {
                    Create("Evt1", "65444"),
                    Create("Evt2", "54654"),
                    Create("Evt3", "23124"),
                    Create("Evt2", "8754"),
                    Create("Evt2", "327d0"),
                    Create("Evt3", "5484."),
                });
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");

            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();
            _store.ClearStreamingLog();

            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();
            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();
            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();

            Assert.AreEqual(0, _store.GetStreamingLog().Count, "Additional calls to event store");
            Assert.AreEqual(4, received.Count, "Received.Count");
            Assert.AreEqual(new EventStoreToken("1"), received[0].Token, "Received[0]");
            Assert.AreEqual(new EventStoreToken("2"), received[1].Token, "Received[1]");
            Assert.AreEqual(new EventStoreToken("3"), received[2].Token, "Received[2]");
            Assert.AreEqual(new EventStoreToken("4"), received[3].Token, "Received[3]");
        }

        [TestMethod]
        public void ReceiveReadyMessageFromBroker()
        {
            var received = new List<EventStoreEvent>();
            _store.AddToStream("aaa", new[]
                {
                    Create("Evt1", "65444"),
                    Create("Evt2", "54654"),
                    Create("Evt3", "23124"),
                    Create("Evt2", "8754"),
                    Create("Evt2", "327d0"),
                    Create("Evt3", "5484."),
                });

            var dest = MessageDestination.For("TestProcess", "__ANY__");
            var msg = new Message { Body = "3\r\n23124", Type = "Evt2", Format = "text" };
            _messaging.Send(dest, msg, () => { }, ThrowError);
            _executor.Process();
            var streamer = _streaming.GetStreamer(new EventStoreToken("7"), "TestProcess");
            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();
            Assert.AreEqual(1, received.Count, "Received.Count");
            Assert.AreEqual(new EventStoreToken("3"), received[0].Token, "Received[0]");
        }

        [TestMethod]
        public void ReceiveMessageWhileWaiting()
        {
            var received = new List<EventStoreEvent>();
            _store.AddToStream("aaa", new[]
                {
                    Create("Evt1", "65444"),
                    Create("Evt2", "54654"),
                    Create("Evt3", "23124"),
                    Create("Evt2", "8754"),
                    Create("Evt2", "327d0"),
                    Create("Evt3", "5484."),
                });

            var dest = MessageDestination.For("TestProcess", "__ANY__");
            var msg = new Message { Body = "3\r\n23124", Type = "Evt2", Format = "text" };
            var streamer = _streaming.GetStreamer(new EventStoreToken("7"), "TestProcess");
            streamer.GetNextEvent(e => received.Add(e), ThrowError, false);
            _executor.Process();
            _messaging.Send(dest, msg, () => { }, ThrowError);
            _executor.Process();
            Assert.AreEqual(1, received.Count, "Received.Count");
            Assert.AreEqual(new EventStoreToken("3"), received[0].Token, "Received[0]");
        }

        private void ThrowError(Exception ex)
        {
            throw ex.PreserveStackTrace();
        }

        private EventStoreEvent Create(string type, string body)
        {
            return new EventStoreEvent
            {
                Body = body,
                Format = "text",
                Type = type
            };
        }
    }
}
