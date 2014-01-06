using System;
using System.Collections.Generic;
using System.Threading;

namespace ServiceLib
{
    public interface IEventStreaming
    {
        IEventStreamer GetStreamer(EventStoreToken token);
    }
    public interface IEventStreamer : IDisposable
    {
        void GetNextEvent(Action<EventStoreEvent> onComplete, Action<Exception> onError, bool withoutWaiting);
    }

    public class EventStreaming : IEventStreaming
    {
        private IEventStoreWaitable _store;
        private IQueueExecution _executor;
        private int _batchSize = 20;

        public EventStreaming(IEventStoreWaitable store, IQueueExecution executor)
        {
            _store = store;
            _executor = executor;
        }

        public EventStreaming BatchSize(int batchSize)
        {
            _batchSize = batchSize;
            return this;
        }

        public IEventStreamer GetStreamer(EventStoreToken token)
        {
            return new EventsStream(this, token);
        }

        private class GetNextEventCompleted : IQueuedExecutionDispatcher
        {
            private Action<EventStoreEvent> _onComplete;
            private EventStoreEvent _completedEvent;

            public GetNextEventCompleted(Action<EventStoreEvent> onComplete, EventStoreEvent completedEvent)
            {
                _onComplete = onComplete;
                _completedEvent = completedEvent;
            }

            public void Execute()
            {
                _onComplete(_completedEvent);
            }
        }

        private class EventsStream : IEventStreamer
        {
            private object _lock;
            private IEventStoreWaitable _store;
            private IQueueExecution _executor;
            private Action<EventStoreEvent> _onComplete;
            private Action<Exception> _onError;
            private bool _nowait;
            private Queue<EventStoreEvent> _queue;
            private EventStoreToken _token;
            private int _batchSize;
            private IDisposable _wait;

            public EventsStream(EventStreaming parent, EventStoreToken token)
            {
                _lock = new object();
                _token = token;
                _store = parent._store;
                _executor = parent._executor;
                _batchSize = parent._batchSize;
                _queue = new Queue<EventStoreEvent>();
            }

            public void GetNextEvent(Action<EventStoreEvent> onComplete, Action<Exception> onError, bool withoutWaiting)
            {
                if (_queue.Count == 0)
                {
                    _onComplete = onComplete;
                    _onError = onError;
                    _nowait = withoutWaiting;
                    if (_nowait)
                        _store.GetAllEvents(_token, _batchSize, true, OnEventsLoaded, OnError);
                    else
                        _wait = _store.WaitForEvents(_token, _batchSize, true, OnEventsLoaded, OnError);
                }
                else
                    _executor.Enqueue(new GetNextEventCompleted(onComplete, _queue.Dequeue()));
            }

            private void OnEventsLoaded(IEventStoreCollection events)
            {
                _wait = null;
                _token = events.NextToken;
                EventStoreEvent firstEvent = null;
                foreach (var evnt in events.Events)
                {
                    if (firstEvent == null)
                        firstEvent = evnt;
                    else
                        _queue.Enqueue(evnt);
                }
                _executor.Enqueue(new GetNextEventCompleted(_onComplete, firstEvent));
            }

            private void OnError(Exception exception)
            {
                _wait = null;
                _executor.Enqueue(_onError, exception);
            }

            public void Dispose()
            {
                var wait = Interlocked.Exchange(ref _wait, null);
                if (wait == null)
                    return;
                wait.Dispose();
                _executor.Enqueue(new GetNextEventCompleted(_onComplete, null));
            }
        }
    }

}
