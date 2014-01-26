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
            private MessageDestination _inputQueue;
            private Message _processedMessage;
            private EventStoreEvent _processedEvent;
            private bool _waitsForMessage, _waitsForEventsStore, _getNextEventActive;
            private IDisposable _cancellableEventStoreWait, _cancellableMessagingWait;

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
                lock (_lock)
                {
                    _getNextEventActive = true;
                    _onComplete = onComplete;
                    _onError = onError;
                    _nowait = withoutWaiting;
                    if (_processedMessage != null)
                        _messaging.MarkProcessed(_processedMessage, MessageDestination.Processed, OnMessageMarkedAsProcessed, OnMessagingError);
                    else
                        GetOrLoadNextEvent();
                }
            }

            private void OnMessageMarkedAsProcessed()
            {
                lock (_lock)
                {
                    _processedMessage = null;
                    GetOrLoadNextEvent();
                }
            }

            private void GetOrLoadNextEvent()
            {
                if (_outputQueue.Count == 0)
                    LoadNextEvents();
                else if (_getNextEventActive)
                {
                    _getNextEventActive = false;
                    _processedEvent = _outputQueue.Dequeue();
                    _executor.Enqueue(new GetNextEventCompleted(_onComplete, _processedEvent));
                }
            }

            private void LoadNextEvents()
            {
                lock (_lock)
                {
                    if (!_getNextEventActive)
                        return;
                    if (!_waitsForMessage)
                    {
                        _waitsForMessage = true;
                        _cancellableMessagingWait = _messaging.Receive(_inputQueue, _nowait, OnMessageReceived, OnMessagesEmpty, OnMessagingError);
                    }
                    if (!_waitsForEventsStore)
                    {
                        _waitsForEventsStore = true;
                        if (_nowait)
                            _store.GetAllEvents(_token, _batchSize, true, OnEventsLoaded, OnEventStoreError);
                        else
                            _cancellableEventStoreWait = _store.WaitForEvents(_token, _batchSize, true, OnEventsLoaded, OnEventStoreError);
                    }
                }
            }

            private void OnMessageReceived(Message msg)
            {
                lock (_lock)
                {
                    _waitsForMessage = false;
                    _cancellableMessagingWait = null;
                    _processedMessage = msg;
                    EventStoreEvent evnt = EventFromMessage(msg);
                    if (evnt == null)
                        _messaging.MarkProcessed(msg, MessageDestination.DeadLetters, OnMessageMarkedAsProcessed, OnMessagingError);
                    else if (_getNextEventActive)
                    {
                        _getNextEventActive = false;
                        _executor.Enqueue(new GetNextEventCompleted(_onComplete, evnt));
                    }
                    else
                    {
                        _outputQueue.Enqueue(evnt);
                    }
                }
            }

            private EventStoreEvent EventFromMessage(Message msg)
            {
                var evnt = new EventStoreEvent();
                evnt.Format = msg.Format;
                evnt.Type = msg.Type;
                var endLine = msg.Body.IndexOf("\r\n");
                if (endLine == -1)
                    return null;
                else
                {
                    evnt.Token = new EventStoreToken(msg.Body.Substring(0, endLine));
                    evnt.Body = msg.Body.Substring(endLine + 2);
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

            private void OnEventsLoaded(IEventStoreCollection events)
            {
                lock (_lock)
                {
                    _cancellableEventStoreWait = null;
                    _waitsForEventsStore = false;
                    _token = events.NextToken;
                    EventStoreEvent firstEvent = null;
                    foreach (var evnt in events.Events)
                    {
                        if (firstEvent == null && _getNextEventActive)
                            firstEvent = evnt;
                        else
                            _outputQueue.Enqueue(evnt);
                    }
                    if (firstEvent != null)
                    {
                        if (_getNextEventActive)
                        {
                            _getNextEventActive = false;
                            _processedEvent = firstEvent;
                            _executor.Enqueue(new GetNextEventCompleted(_onComplete, firstEvent));
                        }
                    }
                    else if (_getNextEventActive)
                    {
                        if (_nowait)
                            OnNothingReceivedNowait();
                        else
                            LoadNextEvents();
                    }
                }
            }

            private void OnMessagesEmpty()
            {
                lock (_lock)
                {
                    _waitsForMessage = false;
                    if (!_getNextEventActive)
                        return;
                    if (_nowait)
                        OnNothingReceivedNowait();
                    else
                        LoadNextEvents();
                }
            }

            private void OnNothingReceivedNowait()
            {
                if (_waitsForMessage || _waitsForEventsStore)
                    return;
                _getNextEventActive = false;
                _processedEvent = null;
                _executor.Enqueue(new GetNextEventCompleted(_onComplete, null));
            }

            private void OnMessagingError(Exception exception)
            {
                lock (_lock)
                {
                    _waitsForMessage = false;
                    if (_getNextEventActive)
                    {
                        _getNextEventActive = false;
                        _executor.Enqueue(_onError, exception);
                    }
                }
            }

            private void OnEventStoreError(Exception exception)
            {
                lock (_lock)
                {
                    _waitsForEventsStore = false;
                    if (_getNextEventActive)
                    {
                        _getNextEventActive = false;
                        _executor.Enqueue(_onError, exception);
                    }
                }
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
                lock (_lock)
                {
                    if (_getNextEventActive)
                    {
                        _getNextEventActive = false;
                        if (_cancellableMessagingWait != null)
                            _cancellableMessagingWait.Dispose();
                        if (_cancellableEventStoreWait != null)
                            _cancellableEventStoreWait.Dispose();
                        _executor.Enqueue(new GetNextEventCompleted(_onComplete, null));
                    }
                    else
                    {
                        if (_cancellableMessagingWait != null)
                            _cancellableMessagingWait.Dispose();
                        if (_cancellableEventStoreWait != null)
                            _cancellableEventStoreWait.Dispose();
                    }
                }
            }
        }
    }

}
