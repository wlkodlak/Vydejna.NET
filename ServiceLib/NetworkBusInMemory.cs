﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ServiceLib
{
    public class NetworkBusInMemory : INetworkBus
    {
        private ConcurrentDictionary<MessageDestination, DestinationContents> _destinations;
        private IQueueExecution _executor;

        public NetworkBusInMemory(IQueueExecution executor, string nodeId)
        {
            _executor = executor;
            _destinations = new ConcurrentDictionary<MessageDestination, DestinationContents>();
        }

        public void Dispose()
        {
        }

        private class DestinationContents
        {
            public readonly MessageDestination Destination;
            public ConcurrentQueue<Message> Incoming;
            public ConcurrentDictionary<string, Message> InProgress;
            public ConcurrentDictionary<string, bool> Subscriptions;
            public ConcurrentBag<Waiter> Waiters;
            public DestinationContents(MessageDestination destination)
            {
                Destination = destination;
                Incoming = new ConcurrentQueue<Message>();
                InProgress = new ConcurrentDictionary<string, Message>();
                Subscriptions = new ConcurrentDictionary<string, bool>();
                Waiters = new ConcurrentBag<Waiter>();
            }
        }

        private class Waiter
        {
            public readonly Action<Message> OnReceived;
            public readonly Action NothingNew;
            public readonly Action<Exception> OnError;

            public Waiter(Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
            {
                OnReceived = onReceived;
                NothingNew = nothingNew;
                OnError = onError;
            }
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
                    contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
                Enqueue(contents, message);
            }
            _executor.Enqueue(onComplete);
        }

        private void Enqueue(DestinationContents contents, Message message)
        {
            Waiter waiter;
            if (contents.Waiters.TryTake(out waiter))
            {
                contents.InProgress[message.MessageId] = message;
                _executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message));
            }
            else
            {
                lock (contents)
                {
                    if (contents.Waiters.TryTake(out waiter))
                    {
                        contents.InProgress[message.MessageId] = message;
                        _executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message)); 
                    }
                    else
                        contents.Incoming.Enqueue(message);
                }
            }
        }

        public void Receive(MessageDestination destination, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
        {
            DestinationContents contents;
            Message message;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
            if (contents.Incoming.TryDequeue(out message))
            {
                contents.InProgress[message.MessageId] = message;
                _executor.Enqueue(new NetworkBusReceiveFinished(onReceived, message));
            }
            else if (nowait)
                _executor.Enqueue(nothingNew);
            else
            {
                lock (contents)
                {
                    if (contents.Incoming.TryDequeue(out message))
                    {
                        contents.InProgress[message.MessageId] = message;
                        _executor.Enqueue(new NetworkBusReceiveFinished(onReceived, message));
                    }
                    else
                        contents.Waiters.Add(new Waiter(onReceived, nothingNew, onError));
                }
            }
        }

        public void Subscribe(string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
            contents.Subscriptions[type] = !unsubscribe;
            _executor.Enqueue(onComplete);
        }

        public void MarkProcessed(Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(message.Destination, out contents))
                contents = _destinations.GetOrAdd(message.Destination, new DestinationContents(message.Destination));
            Message removed;
            contents.InProgress.TryRemove(message.MessageId, out removed);
            _executor.Enqueue(onComplete);
        }

        public void DeleteAll(MessageDestination destination, Action onComplete, Action<Exception> onError)
        {
            DestinationContents contents;
            if (!_destinations.TryGetValue(destination, out contents))
                contents = _destinations.GetOrAdd(destination, new DestinationContents(destination));
            contents.InProgress = new ConcurrentDictionary<string, Message>();
            contents.Incoming = new ConcurrentQueue<Message>();
            _executor.Enqueue(onComplete);
        }
    }
}