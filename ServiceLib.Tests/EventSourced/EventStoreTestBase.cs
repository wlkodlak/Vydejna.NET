using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public abstract class EventStoreTestBase
    {
        protected TestExecutor Executor;
        protected IEventStoreWaitable Store;

        [TestInitialize]
        public void Initialize()
        {
            Executor = new TestExecutor();
            InitializeCore();
            Store = GetEventStore();
        }

        protected abstract IEventStoreWaitable GetEventStore();
        protected virtual void InitializeCore() { }

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
            Assert.IsNotNull(allEvents, "Returned stream NULL");
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
            Assert.IsNotNull(allEvents, "Returned stream NULL");
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

            Assert.IsNotNull(allEvents, "Returned stream NULL");
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

            Assert.IsNotNull(allEvents, "Returned stream NULL");
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
            Assert.IsNotNull(events, "Awaited events");
            Assert.AreEqual(2, events.Events.Count);
            foreach (var evnt in events.Events)
                Assert.IsNotNull(evnt.Body);
        }

        [TestMethod]
        public void LoadingNonexistentSnapshot()
        {
            var oldEvents = new[] { 
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "00" },
                new EventStoreEvent { Type = "TypeA", Format = "text", Body = "01" },
            };
            AddToStream("stream-01", oldEvents, EventStoreVersion.Any);
            Assert.IsNull(LoadSnapshot("stream-01"));
        }

        [TestMethod]
        public void SavingSnapshotFillsStreamName()
        {
            var snapshot = new EventStoreSnapshot { Type = "SnapshotA", Format = "text", Body = "SN01" };
            SaveSnapshot("stream-01", snapshot);
            Assert.AreEqual("stream-01", snapshot.StreamName);
        }

        [TestMethod]
        public void LoadingSavedSnapshot()
        {
            var originalSnapshot = new EventStoreSnapshot { Type = "SnapshotA", Format = "text", Body = "SN01" };
            SaveSnapshot("stream-01", originalSnapshot);
            var loadedSnapshot = LoadSnapshot("stream-01");
            Assert.IsNotNull(loadedSnapshot, "No snapshot");
            Assert.AreEqual("stream-01", loadedSnapshot.StreamName, "StreamName");
            Assert.AreEqual("SnapshotA", loadedSnapshot.Type, "Type");
            Assert.AreEqual("text", loadedSnapshot.Format, "Format");
            Assert.AreEqual("SN01", loadedSnapshot.Body, "Body");
        }

        [TestMethod]
        public void LoadingOverwrittenSnapshot()
        {
            var originalSnapshot = new EventStoreSnapshot { Type = "SnapshotA", Format = "text", Body = "SN01" };
            SaveSnapshot("stream-01", originalSnapshot);
            var newSnapshot = new EventStoreSnapshot { Type = "SnapshotB", Format = "text", Body = "SN99" };
            SaveSnapshot("stream-01", newSnapshot);
            var loadedSnapshot = LoadSnapshot("stream-01");
            Assert.IsNotNull(loadedSnapshot, "No snapshot");
            Assert.AreEqual("stream-01", loadedSnapshot.StreamName, "StreamName");
            Assert.AreEqual("SnapshotB", loadedSnapshot.Type, "Type");
            Assert.AreEqual("text", loadedSnapshot.Format, "Format");
            Assert.AreEqual("SN99", loadedSnapshot.Body, "Body");
        }

        protected IEventStoreStream ReadStream(string name, int minVersion, int maxCount)
        {
            IEventStoreStream eventStream = null;
            Store.ReadStream(name, minVersion, maxCount, true, s => eventStream = s, ThrowError);
            Executor.Process();
            return eventStream;
        }

        protected string AddToStream(string name, IList<EventStoreEvent> events, EventStoreVersion version)
        {
            string outcome = null;
            Store.AddToStream(name, events, version, () => outcome = "saved", () => outcome = "conflict", ThrowError);
            Executor.Process();
            return outcome;
        }

        protected IEventStoreCollection GetAllEvents(EventStoreToken token)
        {
            IEventStoreCollection result = null;
            Store.GetAllEvents(token, int.MaxValue, false, r => result = r, ThrowError);
            Executor.Process();
            return result;
        }

        protected EventStoreToken GetTokenForEventNumber(int index)
        {
            var events = GetAllEvents(EventStoreToken.Initial);
            Assert.IsNotNull(events, "GetAllEvents returned null");
            return events.Events.Skip(index).First().Token;
        }

        protected EventStoreSnapshot LoadSnapshot(string name)
        {
            bool loaded = false;
            EventStoreSnapshot snapshot = null;
            Store.LoadSnapshot(name, s => { snapshot = s; loaded = true; }, ThrowError);
            Executor.Process();
            Assert.IsTrue(loaded, "Snapshot {0} not loaded", name);
            return snapshot;
        }

        protected void SaveSnapshot(string name, EventStoreSnapshot snapshot)
        {
            string outcome = null;
            Store.SaveSnapshot(name, snapshot, () => outcome = "saved", ThrowError);
            Executor.Process();
            Assert.AreEqual("saved", outcome, "Snapshot save outcome");
        }

        private IEventStoreCollection _waitResult;
        private IDisposable _currentWait;
        private Exception _waitException;
        private ManualResetEventSlim _waitMre;

        protected void StartWaitingForEvents(EventStoreToken token)
        {
            _waitMre = new ManualResetEventSlim();
            _waitResult = null;
            _waitException = null;
            _currentWait = Store.WaitForEvents(token, int.MaxValue, false, 
                r => { _waitResult = r; _waitMre.Set(); }, 
                ex => { _waitException = ex; _waitMre.Set(); });
            Executor.Process();
        }

        protected IEventStoreCollection GetAwaitedEvents()
        {
            for (int i = 0; i < 3; i++)
            {
                Executor.Process();
                if (_waitMre.Wait(30))
                    break;
            }
            if (_waitException != null)
                ThrowError(_waitException);
            return _waitResult;
        }

        protected void StopWaiting()
        {
            _currentWait.Dispose();
            for (int i = 0; i < 3; i++)
            {
                Executor.Process();
                if (_waitMre.Wait(30))
                    break;
            }
            if (_waitException != null)
                ThrowError(_waitException);
        }

        protected void ThrowError(Exception ex)
        {
            throw ex;
        }
    }
}
