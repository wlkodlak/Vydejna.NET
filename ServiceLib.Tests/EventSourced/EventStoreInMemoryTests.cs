using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using System.Collections.Generic;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStoreInMemoryTests
    {
        private TestExecutor _executor;
        private EventStoreInMemory _store;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _store = new EventStoreInMemory(_executor);
        }

        [TestMethod]
        public void ReadAllEventsFromNonexistentStream()
        {
            var eventStream = ReadStream("stream-01", 1, int.MaxValue);
            
            Assert.IsNotNull(eventStream, "Stream");
            Assert.AreEqual(0, eventStream.StreamVersion, "Stream version");
            Assert.AreEqual(0, eventStream.Events.Count, "Events count");
        }

        [TestMethod]
        public void AddToNonexistentStream()
        {
            var newEvents = new[] { new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" } };

            var outcome = AddToStream("stream-01", newEvents, EventStoreVersion.New);
            
            Assert.AreEqual("saved", outcome, "Outcome");
            foreach (var evnt in newEvents)
            {
                Assert.AreEqual("stream-01", evnt.StreamName, "Stream name");
                Assert.AreNotEqual(0, evnt.StreamVersion, "Stream version");
                Assert.AreNotEqual(EventStoreToken.Initial, evnt.Token ?? EventStoreToken.Initial, "Token");
            }
        }

        [TestMethod]
        public void AddToExistingStream()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" } 
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var newEvents = new[] { new EventStoreEvent { Type = "TypeB", Format = "text", Body = "03" } };

            var outcome = AddToStream("stream-01", newEvents, EventStoreVersion.At(2));

            Assert.AreEqual("saved", outcome, "Outcome");
            var allEvents = ReadStream("stream-01", 1, int.MaxValue);
            Assert.AreEqual(3, allEvents.Events.Count, "All events count");
        }

        [TestMethod]
        public void ConflictWhenAddingToExistingStream()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" } 
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var newEvents = new[] { new EventStoreEvent { Type = "TypeB", Format = "text", Body = "03" } };

            var outcome = AddToStream("stream-01", newEvents, EventStoreVersion.At(1));

            Assert.AreEqual("conflict", outcome, "Outcome");
            var allEvents = ReadStream("stream-01", 1, int.MaxValue);
            Assert.AreEqual(2, allEvents.Events.Count, "All events count");
        }

        [TestMethod]
        public void ReadAllFromExistingStream()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" } 
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);

            var allEvents = ReadStream("stream-01", 1, int.MaxValue);

            Assert.AreEqual(2, allEvents.Events.Count, "All events count");
            Assert.AreEqual("01", allEvents.Events[0].Body, "Body[0]");
            Assert.AreEqual("02", allEvents.Events[1].Body, "Body[1]");
        }

        [TestMethod]
        public void ReadLaterEventsFromExistingStream()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" } 
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);

            var allEvents = ReadStream("stream-01", 2, int.MaxValue);

            Assert.AreEqual(1, allEvents.Events.Count, "Retrieved events count");
            Assert.AreEqual(2, allEvents.Events[0].StreamVersion, "Version[0]");
            Assert.AreEqual("02", allEvents.Events[0].Body, "Body[0]");
        }

        [TestMethod]
        public void GetAllEventsFromEmptyStore()
        {
            var events = GetAllEvents(EventStoreToken.Initial);
            Assert.IsNotNull(events, "Retrieved collection");
            Assert.AreEqual(0, events.Events.Count, "Events count");
            Assert.AreEqual(EventStoreToken.Initial, events.NextToken, "Next token");
        }

        [TestMethod]
        public void GetAllEventsFromFullStore()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" } 
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);

            var events = GetAllEvents(EventStoreToken.Initial);
            Assert.IsNotNull(events, "Retrieved collection");
            Assert.AreEqual(2, events.Events.Count, "Events count");
            Assert.AreNotEqual(EventStoreToken.Initial, events.NextToken, "Next token");
        }

        [TestMethod]
        public void GetSomeEventsFromFullStore()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "03" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "04" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "05" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "06" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var token = GetTokenForEventNumber(4);

            var events = GetAllEvents(token);
            Assert.IsNotNull(events, "Retrieved collection");
            Assert.AreEqual(2, events.Events.Count, "Events count");
            Assert.AreNotEqual(EventStoreToken.Initial, events.NextToken, "Next token");
        }

        [TestMethod]
        public void GetZeroEventsBecauseTokenWasAtTheEnd()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "03" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "04" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "05" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "06" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var token = GetTokenForEventNumber(6);

            var events = GetAllEvents(token);
            Assert.IsNotNull(events, "Retrieved collection");
            Assert.AreEqual(0, events.Events.Count, "Events count");
            Assert.AreEqual(token, events.NextToken, "Next token");
        }

        [TestMethod]
        public void WaitingDoesNotStartIfEventsAreAvailable()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "03" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "04" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "05" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "06" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var token = GetTokenForEventNumber(3);

            StartWaitingForEvents(token);
            var events = GetAwaitedEvents();
            Assert.IsNotNull(events, "Retrieved collection");
            Assert.AreEqual(3, events.Events.Count, "Events count");
        }

        [TestMethod]
        public void WaitingIsCancelledByDispose()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var token = GetTokenForEventNumber(1);
            var newEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" },
            };

            StartWaitingForEvents(token);
            var events = GetAwaitedEvents();
            Assert.IsNull(events, "No events should be available yet");

            StopWaiting();
            
            AddToStream("stream-01", newEvents, EventStoreVersion.Any);
            events = GetAwaitedEvents();
            Assert.IsNull(events, "No events should be retrieved as waiting was cancelled");
        }

        [TestMethod]
        public void WaitingIsCompletedWhenEventsArrive()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            var token = GetTokenForEventNumber(1);
            var newEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "02" },
            };
            StartWaitingForEvents(token);
            var events = GetAwaitedEvents();
            Assert.IsNull(events, "No events should be available yet");
            AddToStream("stream-01", newEvents, EventStoreVersion.Any);

            events = GetAwaitedEvents();
            
            Assert.IsNotNull(events, "Events should be retrieved as they are now available");
            Assert.AreEqual(1, events.Events.Count, "New events count");
        }

        [TestMethod]
        public void EventBodiesMustBePresentAfterLoadBodies()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            StartWaitingForEvents(EventStoreToken.Initial);
            var events = GetAwaitedEvents();
            Assert.AreEqual(2, events.Events.Count);
            foreach (var evnt in events.Events)
                Assert.IsNotNull(evnt.Body);
        }

        private IEventStoreStream ReadStream(string name, int minVersion, int maxCount)
        {
            IEventStoreStream eventStream = null;
            _store.ReadStream(name, minVersion, maxCount, true, s => eventStream = s, ThrowError);
            _executor.Process();
            return eventStream;
        }

        private string AddToStream(string name, IList<EventStoreEvent> events, EventStoreVersion version)
        {
            string outcome = null;
            _store.AddToStream(name, events, version, () => outcome = "saved", () => outcome = "conflict", ThrowError);
            _executor.Process();
            return outcome;
        }

        private IEventStoreCollection GetAllEvents(EventStoreToken token)
        {
            IEventStoreCollection result = null;
            _store.GetAllEvents(token, int.MaxValue, false, r => result = r, ThrowError);
            _executor.Process();
            return result;
        }

        private EventStoreToken GetTokenForEventNumber(int index)
        {
            var events = GetAllEvents(EventStoreToken.Initial);
            return events.Events.Skip(index).First().Token;
        }

        private IEventStoreCollection _waitResult;
        private IDisposable _currentWait;

        private void StartWaitingForEvents(EventStoreToken token)
        {
            _waitResult = null;
            _currentWait = _store.WaitForEvents(token, int.MaxValue, false, r => _waitResult = r, ThrowError);
            _executor.Process();
        }

        private IEventStoreCollection GetAwaitedEvents()
        {
            _executor.Process();
            return _waitResult;
        }

        private void StopWaiting()
        {
            _currentWait.Dispose();
            _executor.Process();
        }

        private void ThrowError(Exception ex)
        {
            throw ex;
        }
    }
}
