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
            private Task<Message> _taskLoadMessage;
            private Task<List<EventStoreEvent>> _taskLoadEvents;
            private CancellationTokenSource _cancel;
            private MessageDestination _inputMessagingQueue;

            public EventsStream(EventStreaming parent, EventStoreToken token, string processName)
            {
                _lock = new object();
                _parent = parent;
                _token = token;
                _processName = processName;
                _cancel = new CancellationTokenSource();
                _prefetchedEvents = new Queue<EventStoreEvent>();
                _inputMessagingQueue = MessageDestination.For(processName, "__ANY__");
            }

            public Task<EventStoreEvent> GetNextEvent(bool withoutWaiting)
            {
                return TaskUtils.FromEnumerable<EventStoreEvent>(GetNextEventInternal(withoutWaiting)).GetTask();
            }

            private IEnumerable<Task> GetNextEventInternal(bool withoutWaiting)
            {
                if (_disposed)
                {
                    var exception = new ObjectDisposedException(_processName ?? "EventStreamer");
                    yield return TaskUtils.FromError<EventStoreEvent>(exception);
                    yield break;
                }
                if (_currentMessage != null)
                {
                    var taskMarkProcessed = _parent._messaging.MarkProcessed(_currentMessage, MessageDestination.Processed);
                    _currentMessage = null;
                    yield return taskMarkProcessed;
                    taskMarkProcessed.Wait();
                }
                if (_prefetchedMessage != null)
                {
                    _currentMessage = _prefetchedMessage;
                    _prefetchedMessage = null;
                    var evnt = EventFromMessage(_currentMessage);
                    yield return TaskUtils.FromResult(evnt);
                    yield break;
                }
                if (_prefetchedEvents.Count > 0)
                {
                    _currentEvent = _prefetchedEvents.Dequeue();
                    yield return TaskUtils.FromResult(_currentEvent);
                    yield break;
                }
                if (_taskLoadMessage == null)
                {
                    _taskLoadMessage = _parent._messaging.Receive(_inputMessagingQueue, false, _cancel.Token);
                    _taskLoadMessage.ContinueWith(GetNextEvent_FromMessaging);
                }
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
                    _cancel.Cancel();
                    _cancel.Dispose();
                }
            }
        }
    }

}
