using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public abstract class EventStoreTestBase
    {
        protected TestScheduler Scheduler;
        protected IEventStoreWaitable Store;

        [TestInitialize]
        public void Initialize()
        {
            Scheduler = new TestScheduler();
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
            try
            {
                /* Here we have two options: one is to return empty list, another is normal task cancellation */
                events = GetAwaitedEvents();
                Assert.IsNotNull(events, "Event list must be present");
                Assert.AreEqual(0, events.Events.Count, "Event list must be empty");
            }
            catch (OperationCanceledException)
            {
            }
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
            return Scheduler.Run(() => Store.ReadStream(name, minVersion, maxCount, true)).Result;
        }

        protected string AddToStream(string name, IList<EventStoreEvent> events, EventStoreVersion version)
        {
            return Scheduler.Run(() => Store.AddToStream(name, events, version)).Result ? "saved" : "conflict";
        }

        protected IEventStoreCollection GetAllEvents(EventStoreToken token)
        {
            return Scheduler.Run(() => Store.GetAllEvents(token, int.MaxValue, false)).Result;
        }

        protected EventStoreToken GetTokenForEventNumber(int index)
        {
            var events = GetAllEvents(EventStoreToken.Initial);
            Assert.IsNotNull(events, "GetAllEvents returned null");
            return events.Events.Skip(index).First().Token;
        }

        protected EventStoreSnapshot LoadSnapshot(string name)
        {
            return Scheduler.Run(() => Store.LoadSnapshot(name)).Result;
        }

        protected void SaveSnapshot(string name, EventStoreSnapshot snapshot)
        {
            Scheduler.Run(() => Store.SaveSnapshot(name, snapshot));
        }

        private Task<IEventStoreCollection> _waitTask;
        private CancellationTokenSource _waitCancellation;

        protected void StartWaitingForEvents(EventStoreToken token)
        {
            _waitCancellation = new CancellationTokenSource();
            _waitTask = Scheduler.Run(() => Store.WaitForEvents(token, int.MaxValue, false, _waitCancellation.Token), false);
        }

        protected IEventStoreCollection GetAwaitedEvents()
        {
            Scheduler.Process();
            if (_waitTask.IsCanceled)
                throw new OperationCanceledException();
            else if (_waitTask.Exception != null)
                throw _waitTask.Exception.InnerException.PreserveStackTrace();
            else if (_waitTask.IsCompleted)
                return _waitTask.Result;
            else
                return null;
        }

        protected void StopWaiting()
        {
            _waitCancellation.Cancel();
            Scheduler.Process();
        }
    }
}
