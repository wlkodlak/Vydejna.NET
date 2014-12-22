using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class NetworkBusInMemory : INetworkBus
    {
        private int _collectingIsSetup;
        private readonly ConcurrentDictionary<MessageDestination, DestinationContents> _destinations;
        private readonly ITime _timeService;
        private readonly int _collectInterval;
        private readonly int _collectTimeout;
        private readonly CancellationTokenSource _cancel;
        private readonly CancellationToken _cancelToken;
        private Task _taskCollect;

        private static readonly NetworkBusInMemoryTraceSource Logger
            = new NetworkBusInMemoryTraceSource("ServiceLib.NetworkBus");

        public NetworkBusInMemory(ITime timeService)
        {
            _timeService = timeService;
            _destinations = new ConcurrentDictionary<MessageDestination, DestinationContents>();
            _collectInterval = 60;
            _collectTimeout = 600;
            _cancel = new CancellationTokenSource();
            _cancelToken = _cancel.Token;
        }

        private void StartCollecting()
        {
            if (Interlocked.Exchange(ref _collectingIsSetup, 1) == 0)
            {
                _taskCollect = DoCollect();
            }
        }

        private async Task DoCollect()
        {
            while (!_cancelToken.IsCancellationRequested)
            {
                await _timeService.Delay(1000 * _collectInterval, _cancelToken);
                using (new LogMethod(Logger, "DoCollect"))
                {
                    var limit = _timeService.GetUtcTime().AddSeconds(-_collectTimeout);
                    foreach (var destination in _destinations.Values)
                    {
                        var old = destination.DeliveredOn.Where(p => p.Value <= limit).Select(p => p.Key).ToList();
                        foreach (var oldKey in old)
                        {
                            DateTime removedDateTime;
                            destination.DeliveredOn.TryRemove(oldKey, out removedDateTime);
                            Message removedMessage;
                            if (destination.InProgress.TryRemove(oldKey, out removedMessage))
                                destination.Incoming.Enqueue(removedMessage);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _collectingIsSetup, 1);
            _cancel.Cancel();
            try
            {
                _taskCollect.Wait(1000);
            }
            catch (AggregateException)
            {
            }
            _cancel.Dispose();
        }

        private class DestinationContents
        {
            public readonly MessageDestination Destination;
            public ConcurrentQueue<Message> Incoming;
            public ConcurrentDictionary<string, Message> InProgress;
            public readonly ConcurrentDictionary<string, DateTime> DeliveredOn;
            public readonly ConcurrentDictionary<string, bool> Subscriptions;
            public readonly ConcurrentBag<Waiter> Waiters;

            public DestinationContents(MessageDestination destination)
            {
                Destination = destination;
                Incoming = new ConcurrentQueue<Message>();
                InProgress = new ConcurrentDictionary<string, Message>();
                DeliveredOn = new ConcurrentDictionary<string, DateTime>();
                Subscriptions = new ConcurrentDictionary<string, bool>();
                Waiters = new ConcurrentBag<Waiter>();
            }
        }

        private class Waiter
        {
            public CancellationToken Cancel;
            public readonly TaskCompletionSource<Message> Task;

            public Waiter(CancellationToken cancel)
            {
                Cancel = cancel;
                Task = new TaskCompletionSource<Message>();
            }
        }

        private void Notify(DestinationContents contents)
        {
            var toBeCancelled = new List<TaskCompletionSource<Message>>();
            var toBeReceived = new List<Tuple<TaskCompletionSource<Message>, Message>>();
            var needsAnotherTry = true;
            var processName = contents.Destination.ProcessName;
            while (needsAnotherTry)
            {
                needsAnotherTry = false;
                lock (contents)
                {
                    if (contents.Waiters.IsEmpty || contents.Incoming.IsEmpty)
                        return;
                    Waiter waiter;
                    while (contents.Waiters.TryTake(out waiter))
                    {
                        if (waiter.Cancel.IsCancellationRequested)
                        {
                            toBeCancelled.Add(waiter.Task);
                        }
                        else
                        {
                            Message message;
                            if (!contents.Incoming.TryDequeue(out message))
                            {
                                contents.Waiters.Add(waiter);
                                break;
                            }
                            else
                            {
                                toBeReceived.Add(Tuple.Create(waiter.Task, message));
                            }
                        }
                    }
                }
                if (toBeCancelled.Count > 0)
                {
                    foreach (var waiter in toBeCancelled)
                    {
                        Logger.WaiterCancelled(processName, waiter.Task.Id);
                        waiter.TrySetCanceled();
                    }
                }
                foreach (var pair in toBeReceived)
                {
                    if (!pair.Item1.TrySetResult(pair.Item2))
                    {
                        Logger.MessageNotDeliveredToCurrentWaiter(processName, pair.Item2, pair.Item1.Task.Id);
                        contents.Incoming.Enqueue(pair.Item2);
                        needsAnotherTry = true;
                    }
                    else
                    {
                        Logger.MessageDelivered(processName, pair.Item2, pair.Item1.Task.Id);
                    }
                }
                if (needsAnotherTry)
                {
                    toBeCancelled.Clear();
                    toBeReceived.Clear();
                }
            }
        }

        private void WaiterCancelled(object param)
        {
            var waiter = (Waiter) param;
            waiter.Task.TrySetResult(null);
        }

        public Task Send(MessageDestination destination, Message message)
        {
            message.Destination = destination;
            message.MessageId = Guid.NewGuid().ToString("N");
            message.Source = "local";
            message.CreatedOn = _timeService.GetUtcTime();
            if (destination == MessageDestination.Processed)
            {
            }
            else if (destination == MessageDestination.Subscribers)
            {
                foreach (var target in _destinations.Values)
                {
                    if (target.Subscriptions.GetOrAdd(message.Type, false))
                    {
                        target.Incoming.Enqueue(message);
                        Logger.MessageBroadcasted(message, target.Destination);
                        Notify(target);
                    }
                }
            }
            else
            {
                DestinationContents contents;
                if (!_destinations.TryGetValue(destination, out contents))
                    contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
                contents.Incoming.Enqueue(message);
                Logger.MessageSent(message, destination);
                Notify(contents);
            }
            return TaskUtils.CompletedTask();
        }

        public async Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            StartCollecting();
            Message message;
            var contents = GetDestinationContents(destination);
            if (contents.Incoming.TryDequeue(out message))
            {
                contents.InProgress[message.MessageId] = message;
                contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                Logger.MessageReceived(destination, message);
                return message;
            }
            else if (nowait)
            {
                Logger.NoMessageAvailable(destination);
                return null;
            }
            else
            {
                var waiter = new Waiter(cancel);
                if (cancel.CanBeCanceled)
                {
                    cancel.Register(WaiterCancelled, waiter);
                }
                Logger.StartedWaitingForMessage(destination, waiter.Task.Task.Id);
                contents.Waiters.Add(waiter);
                Notify(contents);
                message = await waiter.Task.Task;
                Logger.MessageReceived(destination, message);
                return message;
            }
        }

        private DestinationContents GetDestinationContents(MessageDestination destination)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
            return contents;
        }

        public Task Subscribe(string type, MessageDestination destination, bool unsubscribe)
        {
            var contents = GetDestinationContents(destination);
            contents.Subscriptions[type] = !unsubscribe;
            Logger.Subscribed(unsubscribe, destination, type);
            return TaskUtils.CompletedTask();
        }

        public Task MarkProcessed(Message message, MessageDestination newDestination)
        {
            var contents = GetDestinationContents(message.Destination);
            Message removed;
            contents.InProgress.TryRemove(message.MessageId, out removed);
            if (newDestination == MessageDestination.DeadLetters)
            {
                contents = GetDestinationContents(newDestination);
                contents.Incoming.Enqueue(removed);
                Logger.MarkedAsDeadLetter(message.Destination, message);
            }
            else if (newDestination == MessageDestination.Processed)
            {
                Logger.MarkedAsProcessed(message.Destination, message);
            }
            return TaskUtils.CompletedTask();
        }

        public Task DeleteAll(MessageDestination destination)
        {
            var contents = GetDestinationContents(destination);
            Logger.DeletedAllMessages(destination.ProcessName);
            contents.InProgress = new ConcurrentDictionary<string, Message>();
            contents.Incoming = new ConcurrentQueue<Message>();
            return TaskUtils.CompletedTask();
        }

        public List<Message> GetContents(MessageDestination destination)
        {
            var contents = GetDestinationContents(destination);
            return contents.Incoming.ToList();
        }

        public List<Message> GetContentsInProgress(MessageDestination destination)
        {
            var contents = GetDestinationContents(destination);
            return contents.InProgress.Values.ToList();
        }
    }

    public class NetworkBusInMemoryTraceSource : TraceSource
    {
        public NetworkBusInMemoryTraceSource(string name)
            : base(name)
        {
        }

        public void WaiterCancelled(string processName, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 27, "Waiter {TaskId} cancelled");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void MessageDelivered(string processName, Message message, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 25, "Message was delivered to waiter");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void MessageNotDeliveredToCurrentWaiter(string processName, Message messageId, int taskId)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 26, "Attempt to deliver message failed, will be retried");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, messageId);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void DeletedAllMessages(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 30, "Deleted all messages");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void MessageBroadcasted(Message message, MessageDestination destination)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 21, "Message {MessageId} sent to {Destination} using broadcast");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void MessageSent(Message message, MessageDestination destination)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 22, "Message {MessageId} sent to {Destination} directly");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void MessageReceived(MessageDestination destination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 11, "Message {MessageId} received");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void NoMessageAvailable(MessageDestination destination)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 12, "No messages in queue");
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void StartedWaitingForMessage(MessageDestination destination, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 13, "Started waiting for message");
            msg.SetProperty("ProcessName", false, destination.ProcessName);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void Subscribed(bool unsubscribe, MessageDestination destination, string type)
        {
            var summary = unsubscribe
                ? "Process {ProcessName} unsubscribed from {Type}"
                : "Process {ProcessName} subscribed to {Type}";
            var msg = new LogContextMessage(TraceEventType.Verbose, 16, summary);
            msg.SetProperty("Action", false, unsubscribe ? "Unsubscribed" : "Subscribed");
            msg.SetProperty("Type", false, type);
            msg.SetProperty("ProcessName", false, destination.ProcessName);
            msg.Log(this);
        }

        public void MarkedAsDeadLetter(MessageDestination originalDestination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 17, "Message {MessageId} marked as dead letter");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("OriginalDestination", false, originalDestination.ProcessName);
            msg.Log(this);
        }

        public void MarkedAsProcessed(MessageDestination originalDestination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 18, "Message {MessageId} marked as processed");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("OriginalDestination", false, originalDestination.ProcessName);
            msg.Log(this);
        }
    }
}