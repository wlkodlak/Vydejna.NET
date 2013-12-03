using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Domain;
using Moq;

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

            public Guid Id { get; set; }
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

            public bool HandlesFormat(string format)
            {
                return format == "text";
            }
        }


        private readonly Guid _aggregateGuid = new Guid("11111111-2222-3333-4444-000000000001");
        private readonly string _streamName = "testaggregate_11111111222233334444000000000001";
        private EventStoreInMemory _store;

        [TestInitialize]
        public void Initialize()
        {
            _store = new EventStoreInMemory();
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
            var getTask = repository.Get(_aggregateGuid);
            var aggregate = getTask.GetAwaiter().GetResult();
            Assert.IsNull(aggregate);
        }

        [TestMethod]
        public void Get_Existing_LoadsEvents()
        {
            var storedEvents = new List<EventStoreEvent>();
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent1", Body = "Contents1" });
            storedEvents.Add(new EventStoreEvent { Type = "TestEvent2", Body = "Contents2" });
            _store.AddToStream(_streamName, storedEvents, EventStoreVersion.Any);
            var repository = GetRepository();

            var aggregate = repository.Get(_aggregateGuid).GetAwaiter().GetResult();

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

            repository.Save(aggregate).GetAwaiter().GetResult();

            Assert.AreEqual(1, aggregate.CommitedVersion, "Committed version");
        }

        [TestMethod]
        public void Save_New_AddsEventsToStore()
        {
            var repository = GetRepository();
            var aggregate = new TestAggregate();
            aggregate.Id = _aggregateGuid;
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            repository.Save(aggregate).GetAwaiter().GetResult();

            var storedEvents = _store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult().Events;
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
            _store.AddToStream(_streamName, initialEvents, EventStoreVersion.Any);

            var repository = GetRepository();
            var aggregate = repository.Get(_aggregateGuid).GetAwaiter().GetResult();
            aggregate.Id = _aggregateGuid;  // ID is not present in test events - must be added manually
            aggregate.Changes.Add(new TestEvent2 { Contents = "NewEvent" });

            repository.Save(aggregate).GetAwaiter().GetResult();

            var storedEvents = _store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult().Events;
            Assert.AreEqual(2, storedEvents.Count, "Count");
            Assert.AreEqual(_streamName, storedEvents[1].StreamName, "StreamName");
            Assert.AreEqual(2, storedEvents[1].StreamVersion, "StreamVersion");
            Assert.AreEqual("TestEvent2", storedEvents[1].Type, "Type");
            Assert.AreEqual("NewEvent", storedEvents[1].Body, "Body");
        }
    }
}
