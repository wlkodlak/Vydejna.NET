using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public class GenericRepositoryTests
    {
        private class TestAggregate : IEventSourcedAggregate
        {
            public List<object> Changes = new List<object>();
            public int CommitedVersion = -1;
            public List<object> AllEvents = new List<object>();
            public List<object> LoadedEvents = new List<object>();
            public TestSnapshot LoadedSnapshot = null;

            public IAggregateId Id { get; set; }
            public int OriginalVersion { get { return AllEvents.Count; } }
            public IList<object> GetChanges() { return Changes; }
            public object CreateSnapshot()
            {
                return new TestSnapshot { Events = AllEvents.ToList(), Version = AllEvents.Count };
            }
            public void CommitChanges(int newVersion)
            {
                CommitedVersion = newVersion;
                AllEvents.AddRange(Changes);
                Changes.Clear();
            }
            public int LoadFromSnapshot(object snapshotRaw)
            {
                var snapshot = snapshotRaw as TestSnapshot;
                if (snapshot == null)
                    return 0;
                LoadedSnapshot = snapshot;
                AllEvents = snapshot.Events.ToList();
                return AllEvents.Count;
            }
            public void LoadFromEvents(IList<object> events)
            {
                LoadedEvents.AddRange(events);
                AllEvents.AddRange(events);
            }
        }

        private class TestRepository : EventSourcedRepository<TestAggregate>
        {
            public TestRepository(IEventStore store, IEventSourcedSerializer serializer)
                : base(store, "testaggregate", serializer)
            {
            }
            protected override TestAggregate CreateAggregate()
            {
                return new TestAggregate();
            }
        }

        private class TestEvent1 { public string Contents; }
        private class TestEvent2 { public string Contents; }
        private class TestSnapshot { public int Version; public List<object> Events; }

        private class TestSerializer : IEventSourcedSerializer
        {
            public object Deserialize(EventStoreEvent evt)
            {
                if (evt.Type == "TestEvent1")
                    return new TestEvent1 { Contents = evt.Body };
                if (evt.Type == "TestEvent2")
                    return new TestEvent2 { Contents = evt.Body };
                return null;
            }

            public void Serialize(object evt, EventStoreEvent stored)
            {
                if (evt is TestEvent1)
                {
                    stored.Type = "TestEvent1";
                    stored.Body = (evt as TestEvent1).Contents;
                }
                else if (evt is TestEvent2)
                {
                    stored.Type = "TestEvent2";
                    stored.Body = (evt as TestEvent2).Contents;
                }
            }

            public object Deserialize(EventStoreSnapshot snapshot)
            {
                if (snapshot == null || snapshot.Format != "text" || snapshot.Type != "TestSnapshot" || string.IsNullOrEmpty(snapshot.Body))
                    return null;
                var result = new TestSnapshot();
                result.Events = new List<object>();
                var events = snapshot.Body.Split(new[] { "\r\n" }, StringSplitOptions.None);
                foreach (var row in events)
                {
                    var eventData = row.Split(new[] { ':' });
                    if (eventData[0] == "TestEvent1")
                        result.Events.Add(new TestEvent1 { Contents = eventData[1] });
                    if (eventData[0] == "TestEvent2")
                        result.Events.Add(new TestEvent2 { Contents = eventData[1] });
                }
                result.Version = result.Events.Count;
                return result;
            }

            public void Serialize(object snapshotRaw, EventStoreSnapshot stored)
            {
                var snapshot = (TestSnapshot)snapshotRaw;

                var rows = new List<string>();
                foreach (var evt in snapshot.Events)
                {
                    if (evt is TestEvent1)
                        rows.Add("TestEvent1:" + (evt as TestEvent1).Contents);
                    if (evt is TestEvent2)
                        rows.Add("TestEvent2:" + (evt as TestEvent2).Contents);
                }

                stored.Format = "text";
                stored.Type = "TestSnapshot";
                stored.Body = string.Join("\r\n", rows);
            }

            public bool HandlesFormat(string format)
            {
                return format == "text";
            }

            public string GetTypeName(Type type)
            {
                if (type == typeof(TestEvent1))
                    return "TestEvent1";
                else if (type == typeof(TestEvent2))
                    return "TestEvent2";
                else if (type == typeof(TestSnapshot))
                    return "TestSnapshot";
                else
                    return null;
            }

            public Type GetTypeFromName(string typeName)
            {
                switch (typeName)
                {
                    case "TestEvent1": return typeof(TestEvent1);
                    case "TestEvent2": return typeof(TestEvent2);
                    case "TestSnapshot": return typeof(TestSnapshot);
                    default: return null;
                }
            }
        }


        private readonly IAggregateId _aggregateGuid = new AggregateIdGuid("11111111-2222-3333-4444-000000000001");
        private readonly string _streamName = "testaggregate_11111111222233334444000000000001";
        private TestScheduler _scheduler;
        private TestEventStore _store;
        private TestTrackSource _tracker;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _store = new TestEventStore();
            _tracker = new TestTrackSource();
        }

        private TestRepository GetRepository()
        {
            var serializer = new TestSerializer();
            var repository = new TestRepository(_store, serializer);
            return repository;
        }

        [TestMethod]
        public void Get_Nonexistent_ReturnsNull()
        {
            var repository = GetRepository();
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            Assert.IsNull(aggregate);
        }

        [TestMethod]
        public void Get_ExistingNoSnapshot_LoadsEvents()
        {
            var storedEvents = new List<EventStoreEvent>();
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" });
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" });
            _store.AddToStream(_streamName, storedEvents);
            var repository = GetRepository();

            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;

            Assert.IsNotNull(aggregate, "No aggregate loaded");
            Assert.AreEqual(2, aggregate.LoadedEvents.Count, "Events count");
            Assert.IsInstanceOfType(aggregate.LoadedEvents[0], typeof(TestEvent1));
            Assert.IsInstanceOfType(aggregate.LoadedEvents[1], typeof(TestEvent2));
        }

        [TestMethod]
        public void Get_ExistingWithSnapshot_ResultSameAsNormally()
        {
            var storedEvents = new List<EventStoreEvent>();
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" });
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" });
            _store.AddToStream(_streamName, storedEvents);
            var storedSnapshot = new EventStoreSnapshot { Type = "TestSnapshot", Format = "text", Body = "TestEvent1:Contents1" };
            _store.AddSnapshot(_streamName, storedSnapshot);
            var repository = GetRepository();

            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;

            Assert.IsNotNull(aggregate, "No aggregate loaded");
            Assert.AreEqual(2, aggregate.AllEvents.Count, "Events count");
            Assert.IsInstanceOfType(aggregate.AllEvents[0], typeof(TestEvent1));
            Assert.IsInstanceOfType(aggregate.AllEvents[1], typeof(TestEvent2));
        }

        [TestMethod]
        public void Get_ExistingWithSnapshot_UsesShortcut()
        {
            var storedEvents = new List<EventStoreEvent>();
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" });
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" });
            _store.AddToStream(_streamName, storedEvents);
            var storedSnapshot = new EventStoreSnapshot { Type = "TestSnapshot", Format = "text", Body = "TestEvent1:Contents1" };
            _store.AddSnapshot(_streamName, storedSnapshot);
            var repository = GetRepository();

            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;

            Assert.IsNotNull(aggregate, "No aggregate loaded");
            Assert.IsNotNull(aggregate.LoadedSnapshot, "Used snapshot");
            Assert.AreEqual(1, aggregate.LoadedEvents.Count, "Loaded events count");
        }

        [TestMethod]
        public void Save_New_CommitsChangesToAggregate()
        {
            var repository = GetRepository();
            var aggregate = new TestAggregate();
            aggregate.Id = _aggregateGuid;
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            Assert.AreEqual(true, outcome, "Outcome");
            Assert.AreEqual(1, aggregate.CommitedVersion, "Committed version");
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_New_AddsEventsToStore()
        {
            var repository = GetRepository();
            var aggregate = new TestAggregate();
            aggregate.Id = _aggregateGuid;
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual(1, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[0].StreamName);
            Assert.AreEqual("TestEvent2", storedEvents[0].Type);
            Assert.AreEqual("NewEvent", storedEvents[0].Body);
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_Existing_AddsEventsToStore()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual(true, outcome, "Outcome");
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent2", storedEvents[1].Type, "Type");
            Assert.AreEqual("NewEvent", storedEvents[1].Body, "Body");
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_Conflict()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            _store.AddToStream(_streamName, new[] { new EventStoreEvent { Type = "TestEvent1", Body = "Contents2" } });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual(false, outcome, "Outcome");
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent1", storedEvents[1].Type, "Type");
            Assert.AreEqual("Contents2", storedEvents[1].Body, "Body");
            Assert.AreEqual(0, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_WithSnapshot_SavesAllEvents()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            repository.SnapshotInterval = 2;
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual(true, outcome, "Outcome");
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent2", storedEvents[1].Type, "Type");
            Assert.AreEqual("NewEvent", storedEvents[1].Body, "Body");
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_WithSnapshot_SavesSnapshotOnInterval()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            repository.SnapshotInterval = 2;
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var snapshots = _store.GetAllSnapshots();
            Assert.AreEqual(true, outcome);
            Assert.AreEqual(1, snapshots.Count, "Count");
            Assert.AreEqual(_streamName, snapshots[0].StreamName, "StreamName");
            Assert.AreEqual("TestSnapshot", snapshots[0].Type, "Type");
            Assert.AreEqual("TestEvent1:Contents1\r\nTestEvent2:NewEvent", snapshots[0].Body, "Body");
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }

        [TestMethod]
        public void Save_WithSnapshot_DoesntSaveSnapshotWhenNotOnInterval()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" },
                new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" } 
            };
            _store.AddToStream(_streamName, initialEvents);
            var storedSnapshot = new EventStoreSnapshot { Type = "TestSnapshot", Format = "text", Body = "TestEvent1:Contents1\r\nTestEvent2:NewEvent" };
            _store.AddSnapshot(_streamName, storedSnapshot);

            var repository = GetRepository();
            repository.SnapshotInterval = 2;
            var aggregate = _scheduler.Run(() => repository.Load(_aggregateGuid)).Result;
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            var outcome = _scheduler.Run(() => repository.Save(aggregate, _tracker)).Result;

            var snapshots = _store.GetAllSnapshots();
            Assert.AreEqual(true, outcome);
            Assert.AreEqual(1, snapshots.Count, "Count");
            Assert.AreEqual(_streamName, snapshots[0].StreamName, "StreamName");
            Assert.AreEqual("TestSnapshot", snapshots[0].Type, "Type");
            Assert.AreEqual("TestEvent1:Contents1\r\nTestEvent2:NewEvent", snapshots[0].Body, "Body");
            Assert.AreEqual(1, _tracker.TrackedEvents, "Number of tracked events");
        }
    }
}
