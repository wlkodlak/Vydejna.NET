using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        Task<EventStoreEvent> GetNextEvent(bool nowait);
        Task MarkAsDeadLetter();
    }

    public class EventStreaming : IEventStreaming
    {
        private readonly IEventStoreWaitable _store;
        private readonly INetworkBus _messaging;
        private int _batchSize = 20;

        public EventStreaming(IEventStoreWaitable store, INetworkBus messaging)
        {
            _store = store;
            _messaging = messaging ?? new NetworkBusNull();
        }

        public EventStreaming BatchSize(int batchSize)
        {
            _batchSize = batchSize;
            return this;
        }

        public IEventStreamer GetStreamer(EventStoreToken token, string processName)
        {
            return new EventsStream(_store, _messaging, _batchSize, token, processName);
        }

        private static EventStoreEvent EventFromMessage(Message msg)
        {
            var evnt = new EventStoreEvent();
            evnt.Format = msg.Format;
            evnt.Type = msg.Type;
            var endLine = msg.Body.IndexOf("\r\n", StringComparison.Ordinal);
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
            private static readonly EventStreamingTraceSource Logger =
                new EventStreamingTraceSource("ServiceLib.EventStreaming");

            private readonly object _lock;
            private readonly IEventStoreWaitable _store;
            private readonly INetworkBus _messaging;
            private readonly int _batchSize;
            private EventStoreToken _token;
            private readonly string _processName;
            private bool _disposed, _isGettingNextEvent;
            private Message _currentMessage;
            private EventStoreEvent _currentEvent;
            private readonly Queue<EventStoreEvent> _prefetchedEvents;
            private readonly CancellationTokenSource _cancel;
            private readonly CancellationToken _cancelToken;
            private readonly MessageDestination _inputMessagingQueue;
            private readonly TaskCompletionSource<object> _taskDisposed;
            private Task<Message> _taskReceiveMessage;
            private Task<IEventStoreCollection> _taskWaitForEvents;

            public EventsStream(
                IEventStoreWaitable store, INetworkBus messaging, int batchSize, EventStoreToken token,
                string processName)
            {
                _lock = new object();
                _store = store;
                _messaging = messaging;
                _batchSize = batchSize;
                _token = token;
                _processName = processName;
                _cancel = new CancellationTokenSource();
                _cancelToken = _cancel.Token;
                _prefetchedEvents = new Queue<EventStoreEvent>();
                _inputMessagingQueue = MessageDestination.For(processName, "__ANY__");
                _taskDisposed = new TaskCompletionSource<object>();
            }

            public async Task<EventStoreEvent> GetNextEvent(bool nowait)
            {
                try
                {
                    LockGetNextEvent();
                    await MarkCurrentMessageAsProcessed();

                    while (true)
                    {
                        _cancelToken.ThrowIfCancellationRequested();

                        var readyEvent = TryToUseReceivedMessage();
                        if (readyEvent != null)
                            return readyEvent;
                        readyEvent = TryToUsePrefetchedEvent();
                        if (readyEvent != null)
                            return readyEvent;
                        readyEvent = TryToUseReceivedEvents();
                        if (readyEvent != null)
                            return readyEvent;

                        if (nowait)
                        {
                            readyEvent = await TryToReceiveMessageWithoutWaiting();
                            if (readyEvent != null)
                                return readyEvent;
                            readyEvent = await TryToGetAllEventsWithoutWaiting();
                            if (readyEvent != null)
                                return readyEvent;
                            return ReturnNoEventAvailable();
                        }
                        else
                        {
                            StartReceivingMessage();
                            StartWaitingForEvents();
                            await Task.WhenAny(_taskReceiveMessage, _taskWaitForEvents, _taskDisposed.Task);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Cancelled(_processName);
                    return null;
                }
                finally
                {
                    UnlockGetNextEvent();
                }
            }

            private async Task MarkCurrentMessageAsProcessed()
            {
                var messageToMarkAsProcesed = _currentMessage;
                _currentMessage = null;
                if (messageToMarkAsProcesed != null)
                {
                    try
                    {
                        await _messaging.MarkProcessed(_currentMessage, MessageDestination.Processed);
                        Logger.MarkedPreviousMessageAsProcessed(_processName, messageToMarkAsProcesed);
                    }
                    catch (Exception exception)
                    {
                        Logger.CouldNotMarkPreviousMessageAsProcessed(_processName, messageToMarkAsProcesed, exception);
                        throw;
                    }
                }
            }

            private EventStoreEvent TryToUseReceivedMessage()
            {
                if (_taskReceiveMessage != null && _taskReceiveMessage.IsCompleted)
                {
                    var taskReceiveMessage = _taskReceiveMessage;
                    _taskReceiveMessage = null;
                    try
                    {
                        var message = taskReceiveMessage.GetAwaiter().GetResult();
                        var readyEvent = ProcessReceivedMessage(message);
                        if (readyEvent != null)
                            return readyEvent;
                    }
                    catch (Exception exception)
                    {
                        Logger.ReceivingMessageFailed(_processName, _inputMessagingQueue, exception);
                        throw;
                    }
                }
                return null;
            }

            private EventStoreEvent TryToUsePrefetchedEvent()
            {
                if (_prefetchedEvents.Count <= 0)
                    return null;
                var receivedEvent = _prefetchedEvents.Dequeue();
                _currentEvent = receivedEvent;
                return receivedEvent;
            }

            private EventStoreEvent TryToUseReceivedEvents()
            {
                if (_taskWaitForEvents != null && _taskWaitForEvents.IsCompleted)
                {
                    var taskWaitForEvents = _taskWaitForEvents;
                    _taskWaitForEvents = null;
                    try
                    {
                        var newEvents = taskWaitForEvents.GetAwaiter().GetResult();
                        var readyEvent = ProcessReceivedEvents(newEvents);
                        if (readyEvent != null)
                            return readyEvent;
                    }
                    catch (Exception exception)
                    {
                        Logger.ReceivingEventsFailed(_processName, _token, _batchSize, exception);
                        throw;
                    }
                }

                return null;
            }

            private async Task<EventStoreEvent> TryToReceiveMessageWithoutWaiting()
            {
                if (_taskReceiveMessage != null)
                    return null;
                var message = await _messaging.Receive(_inputMessagingQueue, true, _cancelToken);
                var readyEvent = ProcessReceivedMessage(message);
                return readyEvent;
            }

            private async Task<EventStoreEvent> TryToGetAllEventsWithoutWaiting()
            {
                if (_taskWaitForEvents != null)
                    return null;
                var newEvents = await _store.GetAllEvents(_token, _batchSize, false);
                var readyEvent = ProcessReceivedEvents(newEvents);
                return readyEvent;
            }

            private EventStoreEvent ProcessReceivedMessage(Message message)
            {
                if (message == null)
                    return null;
                _currentMessage = message;
                var receivedEvent = EventFromMessage(message);
                Logger.ReturnedMessage(_processName, message, receivedEvent);
                return receivedEvent;
            }

            private EventStoreEvent ProcessReceivedEvents(IEventStoreCollection newEvents)
            {
                Logger.ReceivedEvents(_processName, _token, _batchSize, newEvents);
                _token = newEvents.NextToken;
                foreach (var receivedEvent in newEvents.Events)
                    _prefetchedEvents.Enqueue(receivedEvent);

                if (_prefetchedEvents.Count > 0)
                {
                    var receivedEvent = _prefetchedEvents.Dequeue();
                    _currentEvent = receivedEvent;
                    Logger.ReturnedEvent(_processName, receivedEvent);
                    return receivedEvent;
                }
                return null;
            }

            private EventStoreEvent ReturnNoEventAvailable()
            {
                Logger.NoEventsAvailableForNowait(_processName);
                return null;
            }

            private void StartReceivingMessage()
            {
                if (_taskReceiveMessage == null)
                {
                    Logger.StartedReceivingMessage(_processName, _inputMessagingQueue);
                    _taskReceiveMessage = _messaging.Receive(_inputMessagingQueue, false, _cancelToken);
                }
            }

            private void StartWaitingForEvents()
            {
                if (_taskWaitForEvents == null)
                {
                    Logger.StartedWaitingForEvents(_processName, _token, _batchSize);
                    _taskWaitForEvents = _store.WaitForEvents(_token, _batchSize, false, _cancelToken);
                }
            }

            private void LockGetNextEvent()
            {
                lock (_lock)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(_processName ?? "EventStreamer");
                    AssertIsNotAlreadyGettingNextEvent();
                    _isGettingNextEvent = true;
                }
            }

            private void AssertIsNotAlreadyGettingNextEvent()
            {
                lock (_lock)
                {
                    if (_isGettingNextEvent)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                "{0} is already in GetNextEvent()",
                                _processName ?? "EventStreamer"));
                    }
                }
            }

            private void UnlockGetNextEvent()
            {
                lock (_lock)
                {
                    _isGettingNextEvent = false;
                }
            }


            public async Task MarkAsDeadLetter()
            {
                AssertIsNotAlreadyGettingNextEvent();
                if (_currentMessage != null)
                {
                    var message = _currentMessage;
                    _currentMessage = null;
                    await _messaging.MarkProcessed(message, MessageDestination.DeadLetters);
                }
                else if (_currentEvent != null)
                {
                    var message = MessageFromEvent(_currentEvent);
                    _currentEvent = null;
                    await _messaging.Send(MessageDestination.DeadLetters, message);
                }
                else
                    throw new InvalidOperationException("EventStreamer: there is no event unfinished event");
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
                _taskDisposed.TrySetResult(null);
                _cancel.Dispose();
            }
        }
    }

    public class EventStreamingTraceSource : TraceSource
    {
        public EventStreamingTraceSource(string name)
            : base(name)
        {
        }

        public void MarkedPreviousMessageAsProcessed(string processName, Message message)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 11, "Message {MessageId} marked as processed in process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, message);
            msg.Log(this);
        }

        public void CouldNotMarkPreviousMessageAsProcessed(string processName, Message message, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 12,
                "Message {MessageId} could not be marked as processed in process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, message);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ReceivedEvents(
            string processName, EventStoreToken token, int batchSize, IEventStoreCollection newEvents)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 1, "Received {EventsCount} events from token {Token} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("BatchSize", false, batchSize);
            msg.SetProperty("EventsCount", false, newEvents.Events.Count);
            msg.Log(this);
        }

        public void ReturnedMessage(string processName, Message message, EventStoreEvent receivedEvent)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 5,
                "Returning event {EventType} based on message {MessageId} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventType", false, receivedEvent.Type);
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.Log(this);
        }

        public void ReturnedEvent(string processName, EventStoreEvent receivedEvent)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 6, "Returning event {EventType} with token {Token} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventType", false, receivedEvent.Type);
            msg.SetProperty("Token", false, receivedEvent.Token);
            msg.Log(this);
        }

        public void NoEventsAvailableForNowait(string processName)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 7, "No events available immediatelly for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void StartedReceivingMessage(string processName, MessageDestination inputMessagingQueue)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 15, "Started waiting for message for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void StartedWaitingForEvents(string processName, EventStoreToken token, int batchSize)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 16, "Started waiting for events for {ProcessName} with token {Token}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void ReceivingMessageFailed(
            string processName, MessageDestination inputMessagingQueue, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 20, "Receiving messages for {ProcessName} failed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Queue", false, inputMessagingQueue);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ReceivingEventsFailed(string processName, EventStoreToken token, int batchSize, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 21, "Receiving events for {ProcessName} failed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("BatchSize", false, batchSize);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void Cancelled(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 8, "GetNextEvent cancelled in {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }
    }
}