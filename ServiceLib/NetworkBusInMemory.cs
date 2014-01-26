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
        private IQueueExecution _executor;
        private ITime _timeService;
        private int _collectInterval, _collectTimeout;
        private IDisposable _emptyDisposable;

        public NetworkBusInMemory(IQueueExecution executor, ITime timeService)
        {
            _executor = executor;
            _timeService = timeService;
            _destinations = new ConcurrentDictionary<MessageDestination, DestinationContents>();
            _collectInterval = 60;
            _collectTimeout = 600;
            _emptyDisposable = new EmptyDisposable();
        }

        private void StartCollecting()
        {
            if (Interlocked.CompareExchange(ref _collectingIsSetup, 1, 0) == 0)
                _timeService.Delay(1000 * _collectInterval, DoCollect);
        }

        private void DoCollect()
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
            _timeService.Delay(1000 * _collectInterval, DoCollect);
        }

        public void Dispose()
        {
        }

        private class DestinationContents
        {
            public readonly NetworkBusInMemory Parent;
            public readonly MessageDestination Destination;
            public ConcurrentQueue<Message> Incoming;
            public ConcurrentDictionary<string, Message> InProgress;
            public ConcurrentDictionary<string, DateTime> DeliveredOn;
            public ConcurrentDictionary<string, bool> Subscriptions;
            public ConcurrentBag<Waiter> Waiters;
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

        private class Waiter : IDisposable
        {
            public readonly DestinationContents Contents;
            public readonly Action<Message> OnReceived;
            public readonly Action NothingNew;
            public readonly Action<Exception> OnError;
            public bool Disposed, Used;

            public Waiter(DestinationContents contents, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
            {
                Contents = contents;
                OnReceived = onReceived;
                NothingNew = nothingNew;
                OnError = onError;
            }

            public void Dispose()
            {
                lock (this)
                {
                    Disposed = true;
                    if (!Used)
                        Contents.Parent._executor.Enqueue(NothingNew);
                }
            }

            public bool TryToUse()
            {
                lock (this)
                {
                    if (Used || Disposed)
                        return false;
                    Used = true;
                    return true;
                }
            }
        }

        private class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }

        public void Send(MessageDestination destination, Message message, Action onComplete, Action<Exception> onError)
        {
            message.Destination = destination;
            message.MessageId = Guid.NewGuid().ToString("N");
            message.Source = "local";
            message.CreatedOn = DateTime.UtcNow;
            if (destination == MessageDestination.Processed)
            {
            }
            else if (destination == MessageDestination.Subscribers)
            {
                foreach (var target in _destinations.Values)
                    if (target.Subscriptions.GetOrAdd(message.Type, false))
                        Enqueue(target, message);
            }
            else
            {
                DestinationContents contents;
                if (!_destinations.TryGetValue(destination, out contents))
                    contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
                Enqueue(contents, message);
            }
            _executor.Enqueue(onComplete);
        }

        private void Enqueue(DestinationContents contents, Message message)
        {
            Waiter waiter;
            bool wasEnqueued = false;
            while (!wasEnqueued)
            {
                if (contents.Waiters.TryTake(out waiter))
                {
                    if (waiter.TryToUse())
                    {
                        wasEnqueued = true;
                        contents.InProgress[message.MessageId] = message;
                        contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                        _executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message));
                    }
                }
                else
                {
                    lock (contents)
                    {
                        if (contents.Waiters.TryTake(out waiter))
                        {
                            if (waiter.TryToUse())
                            {
                                wasEnqueued = true;
                                contents.InProgress[message.MessageId] = message;
                                contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                                _executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message));
                            }
                        }
                        else
                        {
                            wasEnqueued = true;
                            contents.Incoming.Enqueue(message);
                        }
                    }
                }
            }
        }

        public IDisposable Receive(MessageDestination destination, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
        {
            StartCollecting();
            DestinationContents contents;
            Message message;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            if (contents.Incoming.TryDequeue(out message))
            {
                contents.InProgress[message.MessageId] = message;
                contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                _executor.Enqueue(new NetworkBusReceiveFinished(onReceived, message));
                return _emptyDisposable;
            }
            else if (nowait)
            {
                _executor.Enqueue(nothingNew);
                return _emptyDisposable;
            }
            else
            {
                lock (contents)
                {
                    if (contents.Incoming.TryDequeue(out message))
                    {
                        contents.InProgress[message.MessageId] = message;
                        contents.DeliveredOn[message.MessageId] = _timeService.GetUtcTime();
                        _executor.Enqueue(new NetworkBusReceiveFinished(onReceived, message));
                        return _emptyDisposable;
                    }
                    else
                    {
                        var waiter = new Waiter(contents, onReceived, nothingNew, onError);
                        contents.Waiters.Add(waiter);
                        return waiter;
                    }
                }
            }
        }

        public void Subscribe(string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            contents.Subscriptions[type] = !unsubscribe;
            _executor.Enqueue(onComplete);
        }

        public void MarkProcessed(Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
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
                contents.Incoming.Enqueue(removed);
            }
            _executor.Enqueue(onComplete);
        }

        public void DeleteAll(MessageDestination destination, Action onComplete, Action<Exception> onError)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(this, destination));
            contents.InProgress = new ConcurrentDictionary<string, Message>();
            contents.Incoming = new ConcurrentQueue<Message>();
            _executor.Enqueue(onComplete);
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
