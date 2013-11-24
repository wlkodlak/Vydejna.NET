using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public class EventStoreInMemoryTests
    {
        [TestMethod]
        public void GetAllEvents_Empty_EmptyResult()
        {
            var store = new EventStoreInMemory();
            
            var events = store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, false)
                .GetAwaiter().GetResult();

            Assert.AreEqual(0, events.Events.Count, "Count");
            Assert.AreEqual(EventStoreToken.Initial, events.NextToken, "NextToken");
        }

        [TestMethod]
        public void AddToStream_Empty_GetAllEventsThenReturnsThose()
        {
            var store = new EventStoreInMemory();
            var eventsToStore = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" }
            };

            store.AddToStream("streamname", eventsToStore, EventStoreVersion.Any).GetAwaiter().GetResult();

            var loadedEvents = store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult();
            Assert.AreEqual(2, loadedEvents.Events.Count, "Events count");
            Assert.AreEqual("TypeName", loadedEvents.Events[0].Type, "Events[0].Type");
            Assert.AreEqual("Event2", loadedEvents.Events[1].Type, "Events[1].Type");
            Assert.AreEqual("Event body", loadedEvents.Events[0].Body, "Events[0].Body");
            Assert.AreEqual("Event body 2", loadedEvents.Events[1].Body, "Events[1].Body");
            Assert.AreEqual("streamname", loadedEvents.Events[0].StreamName, "Events[0].StreamName");
            Assert.AreEqual("streamname", loadedEvents.Events[1].StreamName, "Events[1].StreamName");
            Assert.AreEqual(1, loadedEvents.Events[0].StreamVersion, "Events[0].StreamVersion");
            Assert.AreEqual(2, loadedEvents.Events[1].StreamVersion, "Events[1].StreamVersion");
            Assert.AreNotEqual(EventStoreToken.Initial, loadedEvents.Events[0].Token, "Events[0].Token");
            Assert.AreNotEqual(EventStoreToken.Initial, loadedEvents.Events[1].Token, "Events[1].Token");
        }

        [TestMethod]
        public void AddToStream_Existing_AppendsEvents()
        {
            var store = new EventStoreInMemory();
            var eventsToStore1 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" }
            };
            var eventsToStore2 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "Type3", Body = "Event body" },
                new EventStoreEvent { Type = "Type4", Body = "Event body 2" }
            };
            store.AddToStream("streamname", eventsToStore1, EventStoreVersion.Any).GetAwaiter().GetResult();

            store.AddToStream("streamname", eventsToStore2, EventStoreVersion.Any).GetAwaiter().GetResult();

            var loadedEvents = store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult();
            Assert.AreEqual(4, loadedEvents.Events.Count, "Count");
        }

        [TestMethod]
        public void AddToStream_AddsMissingMetadataToPassedEvents()
        {
            var store = new EventStoreInMemory();
            var eventsToStore = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" }
            };
            store.AddToStream("streamname", eventsToStore, EventStoreVersion.Any).GetAwaiter().GetResult();
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual("streamname", eventsToStore[i].StreamName, "{0}.StreamName", i);
                Assert.AreEqual(i + 1, eventsToStore[i].StreamVersion, "{0}.StreamVersion", i);
                Assert.AreNotEqual(EventStoreToken.Initial, eventsToStore[i].Token, "{0}.Token", i);
            }
            

        }

        [TestMethod]
        public void AddToStream_MatchingVersion_AddsEvents()
        {
            var store = new EventStoreInMemory();
            var eventsToStore1 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" }
            };
            var eventsToStore2 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "Type3", Body = "Event body" },
                new EventStoreEvent { Type = "Type4", Body = "Event body 2" }
            };
            store.AddToStream("streamname", eventsToStore1, EventStoreVersion.Any).GetAwaiter().GetResult();

            store.AddToStream("streamname", eventsToStore2, EventStoreVersion.Number(2)).GetAwaiter().GetResult();

            var loadedEvents = store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult();
            Assert.AreEqual(4, loadedEvents.Events.Count, "Count");
        }

        [TestMethod]
        public void AddToStream_NotMatchingVersion_ThrowsWithoutAddingEvents()
        {
            var store = new EventStoreInMemory();
            var eventsToStore1 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" }
            };
            var eventsToStore2 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "Type3", Body = "Event body" },
                new EventStoreEvent { Type = "Type4", Body = "Event body 2" }
            };
            store.AddToStream("streamname", eventsToStore1, EventStoreVersion.Any).GetAwaiter().GetResult();

            try
            {
                store.AddToStream("streamname", eventsToStore2, EventStoreVersion.EmptyStream).GetAwaiter().GetResult();
                Assert.Fail("Should throw");
            }
            catch (InvalidOperationException)
            {
            }

            var loadedEvents = store
                .GetAllEvents(EventStoreToken.Initial, int.MaxValue, true)
                .GetAwaiter().GetResult();
            Assert.AreEqual(2, loadedEvents.Events.Count, "Count");
        }

        [TestMethod]
        public void ReadStream_WithTwoExistingStreamsAndSpecifiedMinVersion_ReadsOnlyItsEventsFromMinVersionUp()
        {
            var store = new EventStoreInMemory();
            var eventsToStore1 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" },
            };
            var eventsToStore2 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "Type3", Body = "Event body" },
            };
            var eventsToStore3 = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "Type4", Body = "Event body 2" }
            };
            store.AddToStream("stream1", eventsToStore1, EventStoreVersion.Any).GetAwaiter().GetResult();
            store.AddToStream("stream2", eventsToStore2, EventStoreVersion.Any).GetAwaiter().GetResult();
            store.AddToStream("stream1", eventsToStore3, EventStoreVersion.Any).GetAwaiter().GetResult();

            var loadedEvents = store
                .ReadStream("stream1", 2, int.MaxValue, true)
                .GetAwaiter().GetResult();
            Assert.AreEqual(2, loadedEvents.Events.Count, "Count");
            for (int i = 0; i < loadedEvents.Events.Count; i++ )
            {
                var evt = loadedEvents.Events[i];
                Assert.AreEqual("stream1", evt.StreamName, "[{0}].StreamName", i);
                Assert.AreEqual(i + 2, evt.StreamVersion, "[{0}].StreamVersion", i);
            }
        }

        [TestMethod]
        public void GetAllEvents_WithToken_ReadsOnlyEventsAfterToken()
        {
            var store = new EventStoreInMemory();
            var eventsToStore = new EventStoreEvent[]
            {
                new EventStoreEvent { Type = "TypeName", Body = "Event body" },
                new EventStoreEvent { Type = "Event2", Body = "Event body 2" },
                new EventStoreEvent { Type = "Type3", Body = "Event body" },
                new EventStoreEvent { Type = "Type4", Body = "Event body 2" }
            };
            store.AddToStream("streamname", eventsToStore, EventStoreVersion.Any).GetAwaiter().GetResult();
            var token = eventsToStore[1].Token;

            var loadedEvents = store
                .GetAllEvents(token, int.MaxValue, true)
                .GetAwaiter().GetResult().Events;
            Assert.AreEqual(2, loadedEvents.Count, "Count");
            Assert.AreEqual("Type3", loadedEvents[0].Type);
        }
    }
}
