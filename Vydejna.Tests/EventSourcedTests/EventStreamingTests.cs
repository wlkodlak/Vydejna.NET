﻿using System;
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
    [TestClass]
    public class EventStreamingTests
    {
        private class TestEvent1 { }
        private class TestEvent2 { }

        private IEventStoreWaitable _store;
        private TypeMapper _typeMapper;
        private EventStreamingIndividual _streamer;
        private CancellationTokenSource _cancel;

        [TestInitialize]
        public void Initialize()
        {
            _store = new TestStore();
            _typeMapper = new TypeMapper();
            _typeMapper.Register(typeof(TestEvent1), "TestEvent1");
            _typeMapper.Register(typeof(TestEvent2), "TestEvent2");
            _streamer = new EventStreamingIndividual(_store, _typeMapper);
            _cancel = new CancellationTokenSource();
        }

        [TestMethod]
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

        private IEnumerable<Type> AllEventTypes()
        {
            yield return typeof(TestEvent1);
            yield return typeof(TestEvent2);
        }

        private class TestStore : IEventStoreWaitable
        {
            public SortedList<string, EventStoreEvent> AllEvents = new SortedList<string, EventStoreEvent>();
            private TaskCompletionSource<object> _waiting = null;

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
                return tcs.Task;
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
