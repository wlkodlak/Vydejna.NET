using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStreamingTests
    {
        private TestScheduler _scheduler;
        private TestEventStore _store;
        private EventStreaming _streaming;
        private VirtualTime _time;
        private NetworkBusInMemory _messaging;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _store = new TestEventStore();
            _time = new VirtualTime();
            _messaging = new NetworkBusInMemory(_time);
            _streaming = new EventStreaming(_store, _messaging).BatchSize(5);
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
            var evnt = _scheduler.Run(() => streamer.GetNextEvent(true)).Result;
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
            var task = _scheduler.Run(() => streamer.GetNextEvent(true), false);
            _store.AddToStream("aaa", new[] { 
                Create("Type2", "5475"),
                Create("Type2", "5475"),
            });
            _scheduler.Process();
            _store.ClearStreamingLog();

            var evnt = _scheduler.Run(() => streamer.GetNextEvent(true)).Result;
            Assert.IsNotNull(evnt, "Event null");
            Assert.AreEqual(new EventStoreToken("5"), evnt.Token, "Token");
            Assert.AreEqual("Get 5 from 4", _store.GetStreamingLog().FirstOrDefault(s => s.StartsWith("Get")), "Log");
        }

        [TestMethod]
        public void ErrorWhenRetrieving()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            var task = _scheduler.Run(() => streamer.GetNextEvent(false), false);
            _store.SendFailure();
            _scheduler.Process();
            Assert.IsTrue(task.IsCompleted, "Completed");
            Assert.IsNotNull(task.Exception, "Exception caught");
        }

        [TestMethod]
        public void RetryingAfterErrorIsPossible()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            var task = _scheduler.Run(() => streamer.GetNextEvent(false), false);
            _store.SendFailure();
            _scheduler.Process();

            _store.AddToStream("aaa", new[] { Create("Type1", "58472") });
            var evnt = _scheduler.Run(() => streamer.GetNextEvent(true), true).Result;

            Assert.IsNotNull(evnt, "Event received");
        }

        [TestMethod]
        public void NullEventOnNowaitWithoutAvailableEvents()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            var evnt = _scheduler.Run(() => streamer.GetNextEvent(true), true).Result;
            Assert.IsNull(evnt, "Received event");
        }

        [TestMethod]
        public void WaitingCallReturnsNullOnDispose()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            var task = _scheduler.Run(() => streamer.GetNextEvent(false), false);
            streamer.Dispose();
            _scheduler.Process();
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status, "Status");
            Assert.IsNull(task.Result, "Received event");
        }

        [TestMethod]
        public void WaitingCallCanHandleNullOnLongPoll()
        {
            var streamer = _streaming.GetStreamer(EventStoreToken.Initial, "TestProcess");
            var task = _scheduler.Run(() => streamer.GetNextEvent(false), false);
            _store.EndLongPoll();
            _scheduler.Process();
            if (task.IsCompleted)
            {
                Assert.IsNull(task.Exception, "Exception");
                Assert.IsNull(task.Result, "Result");
            }
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

            received.Add(_scheduler.Run(() => streamer.GetNextEvent(false), true).Result);
            _store.ClearStreamingLog();

            received.Add(_scheduler.Run(() => streamer.GetNextEvent(false), true).Result);
            received.Add(_scheduler.Run(() => streamer.GetNextEvent(false), true).Result);
            received.Add(_scheduler.Run(() => streamer.GetNextEvent(false), true).Result);

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
            _scheduler.Run(() => _messaging.Send(dest, msg), true);
            var streamer = _streaming.GetStreamer(new EventStoreToken("7"), "TestProcess");
            received.Add(_scheduler.Run(() => streamer.GetNextEvent(false), true).Result);
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
            var taskReceive = _scheduler.Run(() => streamer.GetNextEvent(false), false);
            _scheduler.Run(() => _messaging.Send(dest, msg), true);
            Assert.IsTrue(taskReceive.IsCompleted, "Receive complete");
            Assert.AreEqual(new EventStoreToken("3"), taskReceive.Result.Token, "Received event");
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
