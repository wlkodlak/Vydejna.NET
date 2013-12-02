using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass, NUnit.Framework.TestFixture]
    public class EventStreamingTests
    {
        private class TestEvent1 { }
        private class TestEvent2 { }

        private TestStore _store;
        private TypeMapper _typeMapper;
        private EventStreamingIndividual _streamer;
        private CancellationTokenSource _cancel;

        [TestInitialize, NUnit.Framework.SetUp]
        public void Initialize()
        {
            _store = new TestStore();
            _typeMapper = new TypeMapper();
            _typeMapper.Register(typeof(TestEvent1), "TestEvent1");
            _typeMapper.Register(typeof(TestEvent2), "TestEvent2");
            _streamer = new EventStreamingIndividual(_store, _typeMapper);
            _cancel = new CancellationTokenSource();
        }

        [TestMethod, NUnit.Framework.Test]
        public void EmptyEventStore()
        {
            var stream = _streamer.GetStreamer(AllEventTypes(), EventStoreToken.Initial, false);
            var task = stream.GetNextEvent(_cancel.Token);
            Assert.IsFalse(task.IsCompleted, "Should wait for next event");
            try
            {
                _cancel.Cancel();
                task.GetAwaiter().GetResult();
                Assert.Fail("Expected cancellation");
            }
            catch (TaskCanceledException)
            {
            }
        }

        [TestMethod, NUnit.Framework.Test]
        public void FiltersEvents()
        {
            _store.AddEvent("TestEvent1", "Body1");
            _store.AddEvent("TestEvent2", "Body2");
            _store.AddEvent("TestEvent3", "Body3");
            _store.AddEvent("TestEvent2", "Body4");
            _store.AddEvent("TestEvent1", "Body5");
            _store.AddEvent("TestEvent2", "Body6");
            _store.AddEvent("TestEvent2", "Body7");
            var stream = _streamer.GetStreamer(AllEventTypes(), EventStoreToken.Initial, false);
            var token = EventStoreToken.Initial;
            var receivedEvents = new List<string>();
            _store.OnWait(() => _cancel.Cancel());
            try
            {
                while (true)
                {
                    var task = stream.GetNextEvent(_cancel.Token);
                    var evt = task.GetAwaiter().GetResult();
                    receivedEvents.Add(evt.Body);
                }
            }
            catch (TaskCanceledException)
            {
            }
            Assert.AreEqual(
                "Body1, Body2, Body4, Body5, Body6, Body7",
                string.Join(", ", receivedEvents));
        }

        [TestMethod, NUnit.Framework.Test]
        public void SendsNullInsteadOfWaitingInRebuildMode()
        {
            _store.AddEvent("TestEvent1", "Body1");
            _store.AddEvent("TestEvent2", "Body2");
            _store.AddEvent("TestEvent1", "Body3");
            _cancel.CancelAfter(1000);
            var stream = _streamer.GetStreamer(AllEventTypes(), EventStoreToken.Initial, true);
            for (int i = 0; i < 3; i++)
                stream.GetNextEvent(_cancel.Token).GetAwaiter().GetResult();
            var rebuildStopEvent = stream.GetNextEvent(_cancel.Token).GetAwaiter().GetResult();
            Assert.IsNull(rebuildStopEvent);
            rebuildStopEvent = stream.GetNextEvent(_cancel.Token).GetAwaiter().GetResult();
            Assert.IsNull(rebuildStopEvent);
        }

        [TestMethod, NUnit.Framework.Test]
        public void WaitingForNewEventsWaitsAfterAllNewEventsAreProcessed()
        {
            int phase = 0;
            var received = new List<EventStoreEvent>();
            _store.AddEvent("TestEvent1", "Body1");
            _store.AddEvent("TestEvent2", "Body2");
            _store.AddEvent("TestEvent1", "Body3");
            _cancel.CancelAfter(1000);
            _store.OnWait(() =>
                {
                    if (phase == 0)
                    {
                        _store.AddEvent("TestEvent2", "Body4");
                        _store.AddEvent("TestEvent2", "Body5");
                        phase = 1;
                    }
                    else if (phase == 1)
                    {
                        _store.AddEvent("TestEvent1", "Body6");
                        phase = 2;
                    }
                    else if (phase == 2)
                    {
                        phase = 3;
                        _cancel.Cancel();
                    }
                });
            var stream = _streamer.GetStreamer(AllEventTypes(), EventStoreToken.Initial, false);
            try{
                for (int i = 0; i < 7; i++)
                    received.Add(stream.GetNextEvent(_cancel.Token).GetAwaiter().GetResult());
                Assert.Fail("Expected cancellation");
            }
            catch (TaskCanceledException)
            {
            }
            Assert.AreEqual(
                "Body1, Body2, Body3, Body4, Body5, Body6",
                string.Join(", ", received.Select(e => e.Body)),
                "[ALL].Body");
            Assert.AreEqual(3, phase, "Phase");
        }

        [TestMethod, NUnit.Framework.Test]
        public void StartWithPassedToken()
        {
            int phase = 0;
            var received = new List<EventStoreEvent>();
            _store.AddEvent("TestEvent1", "Body1");
            _store.AddEvent("TestEvent2", "Body2");
            _store.AddEvent("TestEvent1", "Body3");
            _cancel.CancelAfter(1000);
            _store.OnWait(() =>
            {
                if (phase == 0)
                {
                    _store.AddEvent("TestEvent2", "Body4");
                    _store.AddEvent("TestEvent2", "Body5");
                    phase = 1;
                }
                else if (phase == 1)
                {
                    phase = 2;
                    _cancel.Cancel();
                }
            });
            var stream = _streamer.GetStreamer(AllEventTypes(), _store.TokenFor(e => e.Body == "Body2"), false);
            try
            {
                for (int i = 0; i < 7; i++)
                    received.Add(stream.GetNextEvent(_cancel.Token).GetAwaiter().GetResult());
                Assert.Fail("Expected cancellation");
            }
            catch (TaskCanceledException)
            {
            }
            Assert.AreEqual(
                "Body3, Body4, Body5",
                string.Join(", ", received.Select(e => e.Body)),
                "[ALL].Body");
            Assert.AreEqual(2, phase, "Phase");
        }

        private IEnumerable<Type> AllEventTypes()
        {
            yield return typeof(TestEvent1);
            yield return typeof(TestEvent2);
        }

        private class TestStore : IEventStoreWaitable
        {
            public SortedList<string, EventStoreEvent> AllEvents = new SortedList<string, EventStoreEvent>();
            private TaskCompletionSource<object> _waiting = null;
            private Action _onWait = null;

            public void AddEvent(string type, string body)
            {
                var evt = new EventStoreEvent();
                evt.Body = body;
                evt.Format = "text";
                evt.StreamName = "ALL";
                evt.StreamVersion = AllEvents.Count + 1;
                var token = evt.StreamVersion.ToString("00000000");
                evt.Token = new EventStoreToken(token);
                evt.Type = type;
                AllEvents.Add(token, evt);
                if (_waiting != null)
                    _waiting.TrySetResult(null);
            }

            public Task WaitForEvents(EventStoreToken token, CancellationToken cancel)
            {
                if (token == EventStoreToken.Initial)
                {
                    if (AllEvents.Count != 0)
                        return TaskResult.GetCompletedTask();
                }
                else
                {
                    int tokenIndex = AllEvents.IndexOfKey(token.ToString());
                    if (tokenIndex >= 0 && (tokenIndex + 1) < AllEvents.Count)
                        return TaskResult.GetCompletedTask();
                }
                if (_waiting != null)
                    _waiting.TrySetException(new InvalidOperationException("Test class supports only one waiter"));
                var tcs = new TaskCompletionSource<object>();
                cancel.Register(() => tcs.TrySetCanceled());
                _waiting = tcs;
                if (_onWait != null)
                    _onWait();
                return tcs.Task;
            }

            public EventStoreToken TokenFor(Func<EventStoreEvent, bool> evt)
            {
                return AllEvents.Values.Where(evt).Select(e => e.Token).FirstOrDefault();
            }

            public void OnWait(Action action)
            {
                _onWait = action;
            }

            public Task AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
            {
                throw new NotSupportedException("This event store should be used only for reading all events");
            }

            public Task<IEventStoreStream> ReadStream(string stream, int minVersion = 0, int maxCount = int.MaxValue, bool loadBody = true)
            {
                throw new NotSupportedException("This event store should be used only for reading all events");
            }

            public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount = int.MaxValue, bool loadBody = false)
            {
                int firstIndex;
                if (token == EventStoreToken.Initial)
                    firstIndex = 0;
                else if (token == EventStoreToken.Current)
                    firstIndex = AllEvents.Count;
                else
                {
                    firstIndex = AllEvents.IndexOfKey(token.ToString());
                    if (firstIndex < 0)
                        firstIndex = int.MaxValue;
                    else
                        firstIndex++;
                }
                var finalEvents = new List<EventStoreEvent>();
                var nextToken = token;
                var hasMore = false;
                for (int index = firstIndex; index < AllEvents.Count; index++)
                {
                    if (maxCount == 0)
                    {
                        hasMore = true;
                        break;
                    }
                    var evt = CloneEvent(AllEvents.Values[index]);
                    nextToken = evt.Token;
                    if (!loadBody)
                        evt.Body = null;
                    finalEvents.Add(evt);
                }
                return TaskResult.GetCompletedTask<IEventStoreCollection>(new EventStoreCollection(finalEvents, nextToken, hasMore));
            }

            private EventStoreEvent CloneEvent(EventStoreEvent evt)
            {
                return new EventStoreEvent()
                {
                    Body = evt.Body,
                    Format = evt.Format,
                    StreamName = evt.StreamName,
                    Token = evt.Token,
                    Type = evt.Type,
                    StreamVersion = evt.StreamVersion
                };
            }

            public Task LoadBodies(IList<EventStoreEvent> events)
            {
                EventStoreEvent fullEvent;
                foreach (var evt in events)
                {
                    if (AllEvents.TryGetValue(evt.Token.ToString(), out fullEvent))
                        evt.Body = fullEvent.Body;
                }
                return TaskResult.GetCompletedTask();
            }
        }
    }
}
