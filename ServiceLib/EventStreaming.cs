using log4net;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventStreaming
    {
        IEventStreamer GetStreamer(EventStoreToken token, string processName);
    }
    public interface IEventStreamer : IDisposable
    {
        Task<EventStoreEvent> GetNextEvent(bool withoutWaiting);
        Task MarkAsDeadLetter();
    }

    public class EventStreaming : IEventStreaming
    {
        private IEventStoreWaitable _store;
        private int _batchSize = 20;
        private INetworkBus _messaging;

        public EventStreaming(IEventStoreWaitable store, INetworkBus messaging)
        {
            _store = store;
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

        private static EventStoreEvent EventFromMessage(Message msg)
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

        private static Message MessageFromEvent(EventStoreEvent evnt)
        {
            var msg = new Message();
            msg.Format = evnt.Format;
            msg.Type = evnt.Type;
            msg.Body = string.Concat(evnt.Token.ToString(), "\r\n", evnt.Body);
            return msg;
        }

        private Task<IEventStoreCollection> GetEvents(EventStoreToken token, bool nowait, CancellationToken cancel)
        {
            if (nowait)
                return _store.GetAllEvents(token, _batchSize, false);
            else
                return _store.WaitForEvents(token, _batchSize, false, cancel);
        }

        private class EventsStream : IEventStreamer
        {
            private object _lock;
            private EventStreaming _parent;
            private EventStoreToken _token;
            private string _processName;
            private bool _disposed;
            private Message _currentMessage;
            private EventStoreEvent _currentEvent;
            private Message _prefetchedMessage;
            private Queue<EventStoreEvent> _prefetchedEvents;
            private bool _isWaitingForMessages, _isWaitingForEvents;
            private CancellationTokenSource _cancel;
            private MessageDestination _inputMessagingQueue;
            private AutoResetEventAsync _waitForSignal;
            private AggregateException _pendingError;
            private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.EventStreaming");

            public EventsStream(EventStreaming parent, EventStoreToken token, string processName)
            {
                _lock = new object();
                _parent = parent;
                _token = token;
                _processName = processName;
                _cancel = new CancellationTokenSource();
                _prefetchedEvents = new Queue<EventStoreEvent>();
                _inputMessagingQueue = MessageDestination.For(processName, "__ANY__");
                _waitForSignal = new AutoResetEventAsync();
            }

            public Task<EventStoreEvent> GetNextEvent(bool withoutWaiting)
            {
                lock (_lock)
                {
                    if (_disposed)
                        return TaskUtils.FromError<EventStoreEvent>(new ObjectDisposedException(_processName ?? "EventStreamer"));
                }
                return TaskUtils.FromEnumerable<EventStoreEvent>(GetNextEventInternal(withoutWaiting)).GetTask();
            }

            private IEnumerable<Task> GetNextEventInternal(bool nowait)
            {
                if (_currentMessage != null)
                {
                    Logger.DebugFormat("{0}: Marking previous message (id {1}) as processed", _processName, _currentMessage.MessageId);
                    var taskMarkProcessed = _parent._messaging.MarkProcessed(_currentMessage, MessageDestination.Processed);
                    _currentMessage = null;
                    yield return taskMarkProcessed;
                    taskMarkProcessed.Wait();
                }
                EventStoreEvent readyEvent = null;
                var cancelToken = _cancel.Token;
                Exception error = null;
                while (readyEvent == null && !cancelToken.IsCancellationRequested)
                {
                    bool startWaitingForEvents = false;
                    bool startWaitingForMessages = false;
                    bool nowaitGetEvents = false;
                    bool nowaitGetMessage = false;
                    lock (_lock)
                    {
                        if (_pendingError != null)
                        {
                            error = _pendingError;
                            _pendingError = null;
                            Logger.DebugFormat("{0}: failed", _processName);
                        }
                        else if (_prefetchedMessage != null)
                        {
                            _currentMessage = _prefetchedMessage;
                            _prefetchedMessage = null;
                            readyEvent = EventFromMessage(_currentMessage);
                            Logger.DebugFormat("{0}: returning prefetched message {1} (type {2})", 
                                _processName, _currentMessage.MessageId, _currentMessage.Type);
                        }
                        else if (_prefetchedEvents.Count > 0)
                        {
                            readyEvent = _currentEvent = _prefetchedEvents.Dequeue();
                            Logger.DebugFormat("{0}: returning prefetched event '{1}' (type {2})", 
                                _processName, readyEvent.Token, readyEvent.Type);
                        }
                        else if (nowait)
                        {
                            nowaitGetEvents = !_isWaitingForEvents;
                            nowaitGetMessage = !_isWaitingForMessages;
                        }
                        else
                        {
                            startWaitingForEvents = !_isWaitingForEvents;
                            startWaitingForMessages = !_isWaitingForMessages;
                            _isWaitingForMessages = _isWaitingForEvents = true;
                        }
                    }
                    if (error != null)
                    {
                        yield return TaskUtils.FromError<EventStoreEvent>(error);
                        yield break;
                    }
                    if (nowaitGetMessage && readyEvent == null)
                    {
                        var taskReceive = _parent._messaging.Receive(_inputMessagingQueue, true, cancelToken);
                        yield return taskReceive;
                        _currentMessage = taskReceive.Result;
                        if (_currentMessage != null)
                        {
                            readyEvent = EventFromMessage(_currentMessage);
                            Logger.DebugFormat("{0}: returning fresh message {1} (type {2})",
                                _processName, _currentMessage.MessageId, _currentMessage.Type);
                        }
                    }
                    if (nowaitGetEvents && readyEvent == null)
                    {
                        var taskGetEvents = _parent.GetEvents(_token, true, cancelToken);
                        yield return taskGetEvents;
                        var loadedEvents = taskGetEvents.Result;
                        foreach (var evnt in loadedEvents.Events)
                            _prefetchedEvents.Enqueue(evnt);
                        _token = loadedEvents.NextToken;
                        if (loadedEvents.Events.Count != 0)
                        {
                            readyEvent = _currentEvent = _prefetchedEvents.Dequeue();
                            Logger.DebugFormat("{0}: returning fresh event '{1}' (type {2})",
                                _processName, readyEvent.Token, readyEvent.Type);
                        }
                    }
                    if (startWaitingForMessages)
                    {
                        Logger.DebugFormat("{0}: starting waiting for messages", _processName);
                        _parent._messaging.Receive(_inputMessagingQueue, false, cancelToken).ContinueWith(GetNextEvent_FromMessaging);
                    }
                    if (startWaitingForEvents)
                    {
                        Logger.DebugFormat("{0}: starting waiting for events", _processName);
                        _parent.GetEvents(_token, false, cancelToken).ContinueWith(GetNextEvent_FromEventStore);
                    }
                    if (nowait || readyEvent != null)
                    {
                        break;
                    }
                    else
                    {
                        var taskWait = _waitForSignal.Wait();
                        yield return taskWait;
                    }
                }
                if (readyEvent == null)
                {
                    Logger.DebugFormat("{0}: returning null", _processName);
                }
                yield return TaskUtils.FromResult(readyEvent);
            }

            private void GetNextEvent_FromMessaging(Task<Message> taskReceive)
            {
                lock (_lock)
                {
                    _isWaitingForMessages = false;
                    if (!taskReceive.IsCanceled)
                    {
                        if (taskReceive.Exception == null)
                        {
                            _prefetchedMessage = taskReceive.Result;
                        }
                        else
                        {
                            _pendingError = taskReceive.Exception;
                        }
                    }
                }
                _waitForSignal.Set();
            }

            private void GetNextEvent_FromEventStore(Task<IEventStoreCollection> taskReceive)
            {
                lock (_lock)
                {
                    _isWaitingForEvents = false;
                    if (!taskReceive.IsCanceled)
                    {
                        if (taskReceive.Exception == null)
                        {
                            var loadedEvents = taskReceive.Result;
                            foreach (var evnt in loadedEvents.Events)
                                _prefetchedEvents.Enqueue(evnt);
                            _token = loadedEvents.NextToken;
                        }
                        else
                        {
                            _pendingError = taskReceive.Exception;
                        }
                    }
                }
                _waitForSignal.Set();
            }

            public Task MarkAsDeadLetter()
            {
                Message message;
                bool exists;
                lock (_lock)
                {
                    if (_currentMessage != null)
                    {
                        message = _currentMessage;
                        _currentMessage = null;
                        exists = true;
                    }
                    else if (_currentEvent != null)
                    {
                        message = MessageFromEvent(_currentEvent);
                        _currentEvent = null;
                        exists = false;
                    }
                    else
                        return TaskUtils.FromError<object>(new InvalidOperationException("EventStreamer: there is no event unfinished event"));
                }
                if (exists)
                {
                    return _parent._messaging.MarkProcessed(message, MessageDestination.DeadLetters);
                }
                else
                {
                    return _parent._messaging.Send(MessageDestination.DeadLetters, message);
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed)
                        return;
                    _disposed = true;
                }
                _cancel.Cancel();
                _cancel.Dispose();
                _waitForSignal.Set();
            }
        }
    }

}
