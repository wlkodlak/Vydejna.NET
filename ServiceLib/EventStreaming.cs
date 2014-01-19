using System;
using System.Collections.Generic;
using System.Threading;

namespace ServiceLib
{
    public interface IEventStreaming
    {
        IEventStreamer GetStreamer(EventStoreToken token, string processName);
    }
    public interface IEventStreamer : IDisposable
    {
        void GetNextEvent(Action<EventStoreEvent> onComplete, Action<Exception> onError, bool withoutWaiting);
        void MarkAsDeadLetter(Action onComplete, Action<Exception> onError);
    }

    public class EventStreaming : IEventStreaming
    {
        private IEventStoreWaitable _store;
        private IQueueExecution _executor;
        private int _batchSize = 20;
        private INetworkBus _messaging;

        public EventStreaming(IEventStoreWaitable store, IQueueExecution executor, INetworkBus messaging)
        {
            _store = store;
            _executor = executor;
            _messaging = messaging;
        }

        public EventStreaming BatchSize(int batchSize)
        {
            _batchSize = batchSize;
            return this;
        }

        public IEventStreamer GetStreamer(EventStoreToken token, string processName)
        {
            return new EventsStream(this, token, processName);
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
            private INetworkBus _messaging;
            private Action<EventStoreEvent> _onComplete;
            private Action<Exception> _onError;
            private bool _nowait;
            private Queue<EventStoreEvent> _outputQueue;
            private EventStoreToken _token;
            private int _batchSize;
            private IDisposable _wait;
            private MessageDestination _inputQueue;
            private Message _processedMessage;
            private EventStoreEvent _processedEvent;

            public EventsStream(EventStreaming parent, EventStoreToken token, string processName)
            {
                _lock = new object();
                _token = token;
                _inputQueue = MessageDestination.For(processName, "__ANY__");
                _store = parent._store;
                _executor = parent._executor;
                _batchSize = parent._batchSize;
                _messaging = parent._messaging;
                _outputQueue = new Queue<EventStoreEvent>();
            }

            public void GetNextEvent(Action<EventStoreEvent> onComplete, Action<Exception> onError, bool withoutWaiting)
            {
                if (_outputQueue.Count == 0)
                {
                    _onComplete = onComplete;
                    _onError = onError;
                    _nowait = withoutWaiting;
                    if (_processedMessage != null)
                        _messaging.MarkProcessed(_processedMessage, MessageDestination.Processed, LoadNextEvents, OnError);
                    else
                        LoadNextEvents();
                }
                else
                {
                    _processedEvent = _outputQueue.Dequeue();
                    _executor.Enqueue(new GetNextEventCompleted(onComplete, _processedEvent));
                }
            }

            private void LoadNextEvents()
            {
                _messaging.Receive(_inputQueue, true, OnMessageReceived, ReadFromEventStore, OnError);
            }

            private void OnMessageReceived(Message msg)
            {
                _processedMessage = msg;
                EventStoreEvent evnt = EventFromMessage(msg);
                if (evnt == null)
                    _messaging.MarkProcessed(msg, MessageDestination.DeadLetters, LoadNextEvents, OnError);
                else
                {
                    _token = evnt.Token;
                    _executor.Enqueue(new GetNextEventCompleted(_onComplete, evnt));
                }
            }

            private EventStoreEvent EventFromMessage(Message msg)
            {
                var evnt = new EventStoreEvent();
                evnt.Format = msg.Format;
                evnt.Type = msg.Type;
                var endLine = evnt.Body.IndexOf("\r\n");
                if (endLine == -1)
                    return null;
                else
                {
                    evnt.Token = new EventStoreToken(evnt.Body.Substring(0, endLine));
                    evnt.Body = evnt.Body.Substring(endLine + 2);
                }
                return evnt;
            }

            private Message MessageFromEvent(EventStoreEvent evnt)
            {
                var msg = new Message();
                msg.Format = evnt.Format;
                msg.Type = evnt.Type;
                msg.Body = string.Concat(evnt.Token.ToString(), "\r\n", evnt.Body);
                return msg;
            }

            private void ReadFromEventStore()
            {
                if (_nowait)
                    _store.GetAllEvents(_token, _batchSize, true, OnEventsLoaded, OnError);
                else
                    _wait = _store.WaitForEvents(_token, _batchSize, true, OnEventsLoaded, OnError);
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
                        _outputQueue.Enqueue(evnt);
                }
                _processedEvent = firstEvent;
                _executor.Enqueue(new GetNextEventCompleted(_onComplete, firstEvent));
            }

            private void OnError(Exception exception)
            {
                _wait = null;
                _executor.Enqueue(_onError, exception);
            }

            public void MarkAsDeadLetter(Action onComplete, Action<Exception> onError)
            {
                if (_processedMessage != null)
                {
                    var message = _processedMessage;
                    _processedMessage = null;
                    _messaging.MarkProcessed(_processedMessage, MessageDestination.DeadLetters, onComplete, onError);
                }
                else if (_processedEvent != null)
                {
                    var message = MessageFromEvent(_processedEvent);
                    _messaging.Send(MessageDestination.DeadLetters, message, onComplete, onError);
                }
                else
                    onComplete();
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
