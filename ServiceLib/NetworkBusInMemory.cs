using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ServiceLib
{
    public class NetworkBusInMemory : INetworkBus
    {
        private int _collectingIsSetup;
        private ConcurrentDictionary<MessageDestination, DestinationContents> _destinations;
        private ITime _timeService;
        private int _collectInterval, _collectTimeout;
        private CancellationTokenSource _cancel;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.NetworkBus");

        public NetworkBusInMemory(ITime timeService)
        {
            _timeService = timeService;
            _destinations = new ConcurrentDictionary<MessageDestination, DestinationContents>();
            _collectInterval = 60;
            _collectTimeout = 600;
            _cancel = new CancellationTokenSource();
        }

        private void StartCollecting()
        {
            if (Interlocked.Exchange(ref _collectingIsSetup, 1) == 0)
            {
                _timeService.Delay(1000 * _collectInterval, _cancel.Token)
                    .ContinueWith(DoCollect, _cancel.Token);
            }
        }

        private void DoCollect(Task task)
        {
            if (task.Exception != null || task.IsCanceled)
                return;
            using (new LogMethod(Logger, "DoCollect"))
            {
                DateTime removedDateTime;
                Message removedMessage;
                var limit = _timeService.GetUtcTime().AddSeconds(-_collectTimeout);
                foreach (var destination in _destinations.Values)
                {
                    var old = destination.DeliveredOn.Where(p => p.Value <= limit).Select(p => p.Key).ToList();
                    foreach (var oldKey in old)
                    {
                        destination.DeliveredOn.TryRemove(oldKey, out removedDateTime);
                        if (destination.InProgress.TryRemove(oldKey, out removedMessage))
                            destination.Incoming.Enqueue(removedMessage);
                    }
                }
                _timeService.Delay(1000 * _collectInterval, _cancel.Token)
                    .ContinueWith(DoCollect, _cancel.Token);
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _collectingIsSetup, 1);
            _cancel.Cancel();
            _cancel.Dispose();
        }

        private class DestinationContents
        {
            public readonly NetworkBusInMemory Parent;
            public readonly MessageDestination Destination;
            public ConcurrentQueue<Message> Incoming;
            public ConcurrentDictionary<string, Message> InProgress;
            public readonly ConcurrentDictionary<string, DateTime> DeliveredOn;
            public readonly ConcurrentDictionary<string, bool> Subscriptions;
            public readonly ConcurrentBag<Waiter> Waiters;

            public DestinationContents(NetworkBusInMemory parent, MessageDestination destination)
            {
                Parent = parent;
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
            public DestinationContents Contents;
            public CancellationToken Cancel;
            public TaskCompletionSource<Message> Task;
            public CancellationTokenRegistration CancelRegistration;

            public Waiter(DestinationContents contents, CancellationToken cancel)
            {
                Contents = contents;
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
                    Message message = null;

                    while (contents.Waiters.TryTake(out waiter))
                    {
                        if (waiter.Cancel.IsCancellationRequested)
                        {
                            toBeCancelled.Add(waiter.Task);
                        }
                        else if (!contents.Incoming.TryDequeue(out message))
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
                if (toBeCancelled.Count > 0)
                {
                    Logger.DebugFormat("{0}: Cancelling {1} receives", processName, toBeCancelled.Count);
                    foreach (var waiter in toBeCancelled)
                    {
                        waiter.TrySetCanceled();
                    }
                }
                foreach (var pair in toBeReceived)
                {
                    Logger.DebugFormat("{0}: Returning message {1}", processName, pair.Item2.MessageId);
                    if (!pair.Item1.TrySetResult(pair.Item2))
                    {
                        contents.Incoming.Enqueue(pair.Item2);
                        needsAnotherTry = true;
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
            var waiter = param as Waiter;
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
                Logger.DebugFormat("Broadcasting message {0}", message.MessageId);
                foreach (var target in _destinations.Values)
                {
                    if (target.Subscriptions.GetOrAdd(message.Type, false))
                    {
                        target.Incoming.Enqueue(message);
                        Notify(target);
                    }
                }
            }
            else
            {
                DestinationContents contents;
                if (!_destinations.TryGetValue(destination, out contents))
                    contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
                Logger.DebugFormat("Sending message {0} to {1}", message.MessageId, destination.ProcessName);
                contents.Incoming.Enqueue(message);
                Notify(contents);
            }
            return TaskUtils.CompletedTask();
        }

        public Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
                return TaskUtils.CancelledTask<Message>();
            StartCollecting();
            DestinationContents contents;
            Message message;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            if (contents.Incoming.TryDequeue(out message))
            {
                contents.InProgress[message.MessageId] = message;
                contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                Logger.DebugFormat("{0}: Returning message {1} (type {2})", 
                    destination.ProcessName, message.MessageId, message.Type);
                return TaskUtils.FromResult(message);
            }
            else if (nowait)
            {
                Logger.DebugFormat("{0}: Returning null", destination.ProcessName);
                return TaskUtils.FromResult<Message>(null);
            }
            else
            {
                var waiter = new Waiter(contents, cancel);
                if (cancel.CanBeCanceled)
                {
                    waiter.CancelRegistration = cancel.Register(WaiterCancelled, waiter);
                }
                contents.Waiters.Add(waiter);
                Notify(contents);
                return waiter.Task.Task;
            }
        }

        public Task Subscribe(string type, MessageDestination destination, bool unsubscribe)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            Logger.DebugFormat(
                unsubscribe ? "{0}: Unsubscribing from {1}" : "{0}: Subscribing to {1}", 
                destination.ProcessName, type);
            contents.Subscriptions[type] = !unsubscribe;
            return TaskUtils.CompletedTask();
        }

        public Task MarkProcessed(Message message, MessageDestination newDestination)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(message.Destination, out contents))
                contents = _destinations.GetOrAdd(message.Destination, new DestinationContents(this, message.Destination));
            Message removed;
            contents.InProgress.TryRemove(message.MessageId, out removed);
            if (newDestination == MessageDestination.DeadLetters)
            {
                if (!_destinations.TryGetValue(newDestination, out contents))
                    contents = _destinations.GetOrAdd(newDestination, new DestinationContents(this, newDestination));
                Logger.DebugFormat("{0}: Marked message {1} as dead letter", message.Destination.ProcessName, message.MessageId);
                contents.Incoming.Enqueue(removed);
            }
            else if (newDestination == MessageDestination.Processed)
            {
                Logger.DebugFormat("{0}: Marked message {1} as processed", message.Destination.ProcessName, message.MessageId);
            }
            return TaskUtils.CompletedTask();
        }

        public Task DeleteAll(MessageDestination destination)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            Logger.DebugFormat("{0}: Deleted all messages", destination.ProcessName);
            contents.InProgress = new ConcurrentDictionary<string, Message>();
            contents.Incoming = new ConcurrentQueue<Message>();
            return TaskUtils.CompletedTask();
        }

        public List<Message> GetContents(MessageDestination destination)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            return contents.Incoming.ToList();
        }

        public List<Message> GetContentsInProgress(MessageDestination destination)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            return contents.InProgress.Values.ToList();
        }
    }
}
