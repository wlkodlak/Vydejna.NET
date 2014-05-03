using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public class GenericRepositoryTests
    {
        private class TestAggregate : IEventSourcedAggregate
        {
            public List<object> Changes = new List<object>();
            public int CommitedVersion = -1;
            public List<object> LoadedEvents = new List<object>();

            public IAggregateId Id { get; set; }
            public int OriginalVersion { get { return LoadedEvents.Count; } }
            public IList<object> GetChanges() { return Changes; }
            public object CreateSnapshot() { return null; }
            public void CommitChanges(int newVersion) { CommitedVersion = newVersion; }
            public int LoadFromSnapshot(object snapshot) { return 0; }
            public void LoadFromEvents(IList<object> events) { LoadedEvents.AddRange(events); }
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
                throw new NotSupportedException();
            }

            public void Serialize(object snapshot, EventStoreSnapshot stored)
            {
                throw new NotSupportedException();
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
                else
                    return null;
            }

            public Type GetTypeFromName(string typeName)
            {
                switch (typeName)
                {
                    case "TestEvent1": return typeof(TestEvent1);
                    case "TestEvent2": return typeof(TestEvent2);
                    default: return null;
                }
            }
        }


        private readonly IAggregateId _aggregateGuid = new AggregateIdGuid("11111111-2222-3333-4444-000000000001");
        private readonly string _streamName = "testaggregate_11111111222233334444000000000001";
        private TestExecutor _executor;
        private TestEventStore _store;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _store = new TestEventStore(_executor);
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
            TestAggregate aggregate = null;
            repository.Load(_aggregateGuid, agg => aggregate = agg, () => aggregate = null, ex => { throw ex; });
            _executor.Process();
            Assert.IsNull(aggregate);
        }

        [TestMethod]
        public void Get_Existing_LoadsEvents()
        {
            var storedEvents = new List<EventStoreEvent>();
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" });
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" });
            _store.AddToStream(_streamName, storedEvents);
            var repository = GetRepository();

            TestAggregate aggregate = null;
            repository.Load(_aggregateGuid, agg => aggregate = agg, () => aggregate = null, ex => { throw ex; });
            _executor.Process();

            Assert.IsNotNull(aggregate, "No aggregate loaded");
            Assert.AreEqual(2, aggregate.LoadedEvents.Count, "Events count");
            Assert.IsInstanceOfType(aggregate.LoadedEvents[0], typeof(TestEvent1));
            Assert.IsInstanceOfType(aggregate.LoadedEvents[1], typeof(TestEvent2));
        }

        [TestMethod]
        public void Save_New_CommitsChangesToAggregate()
        {
            var repository = GetRepository();
            var aggregate = new TestAggregate();
            aggregate.Id = _aggregateGuid;
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });
            string outcome = null;

            repository.Save(aggregate, () => outcome = "saved", () => outcome = "conflict", ex => { throw ex; });
            _executor.Process();

            Assert.AreEqual("saved", outcome, "Outcome");
            Assert.AreEqual(1, aggregate.CommitedVersion, "Committed version");
        }

        [TestMethod]
        public void Save_New_AddsEventsToStore()
        {
            var repository = GetRepository();
            var aggregate = new TestAggregate();
            aggregate.Id = _aggregateGuid;
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            repository.Save(aggregate, () => { }, () => { }, ex => { throw ex; });
            _executor.Process();

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual(1, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[0].StreamName);
            Assert.AreEqual("TestEvent2", storedEvents[0].Type);
            Assert.AreEqual("NewEvent", storedEvents[0].Body);
        }

        [TestMethod]
        public void Save_Existing_AddsEventsToStore()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            TestAggregate aggregate = null;
            repository.Load(_aggregateGuid, agg => aggregate = agg, () => aggregate = null, ex => { throw ex; });
            _executor.Process();
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            string outcome = null;
            repository.Save(aggregate, () => outcome = "saved", () => outcome = "conflict", ex => { throw ex; });
            _executor.Process();

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual("saved", outcome, "Outcome");
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent2", storedEvents[1].Type, "Type");
            Assert.AreEqual("NewEvent", storedEvents[1].Body, "Body");
        }

        [TestMethod]
        public void Save_Conflict()
        {
            var initialEvents = new EventStoreEvent[] { 
                new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" } 
            };
            _store.AddToStream(_streamName, initialEvents);

            var repository = GetRepository();
            TestAggregate aggregate = null;
            repository.Load(_aggregateGuid, agg => aggregate = agg, () => aggregate = null, ex => { throw ex; });
            _executor.Process();
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            _store.AddToStream(_streamName, new[] { new EventStoreEvent { Type = "TestEvent1", Body = "Contents2" }});

            string outcome = null;
            repository.Save(aggregate, () => outcome = "saved", () => outcome = "conflict", ex => { throw ex; });
            _executor.Process();

            var storedEvents = _store.GetAllEvents();
            Assert.AreEqual("conflict", outcome, "Outcome");
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent1", storedEvents[1].Type, "Type");
            Assert.AreEqual("Contents2", storedEvents[1].Body, "Body");
        }
    }
}
