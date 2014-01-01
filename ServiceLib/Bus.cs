using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IPublisher
    {
        void Publish<T>(T message);
    }

    public interface ISubscribable
    {
        IHandleRegistration<T> Subscribe<T>(IHandle<T> handler);
    }

    public interface IBus : IPublisher, ISubscribable
    {
    }

    public static class BusExtensions
    {
        public static IHandleRegistration<T> Subscribe<T>(this ISubscribable self, Action<T> handler)
        {
            return self.Subscribe<T>(new DelegatedSyncHandler<T>(handler));
        }

        private class DelegatedSyncHandler<T> : IHandle<T>
        {
            private Action<T> _handler;

            public DelegatedSyncHandler(Action<T> handler)
            {
                _handler = handler;
            }

            public void Handle(T message)
            {
                _handler(message);
            }
        }
    }

    public static class SystemEvents
    {
        public class SystemInit { }
        public class SystemStarted { }
        public class SystemShutdown { }
    }

    public abstract class AbstractBus : IBus
    {
        private string _name;
        private ISubscriptionManager _subscriptions;
        private log4net.ILog _log;

        public AbstractBus(ISubscriptionManager subscribtions, string name)
        {
            _name = name ?? "Bus";
            _subscriptions = subscribtions;
            _log = log4net.LogManager.GetLogger(name);
        }

        public void Publish<T>(T message)
        {
            var messagesToQueue = _subscriptions.FindHandlers(message.GetType()).Select(s => new QueuedMessage(s, message, OnError)).ToList();
            PublishCore(messagesToQueue);
        }

        public IHandleRegistration<T> Subscribe<T>(IHandle<T> handler)
        {
            return _subscriptions.Register(handler);
        }

        protected abstract void PublishCore(ICollection<QueuedMessage> messages);
        
        protected virtual void OnError(QueuedMessage message, Exception exception)
        {
            _log.WarnFormat("When handling {0} exception occurred: {1}", message.Message.GetType().Name, exception);
        }

        protected class QueuedMessage
        {
            private object _message;
            private ISubscription _handler;
            private Action<QueuedMessage, Exception> _onError;

            public QueuedMessage(ISubscription subscription, object message, Action<QueuedMessage, Exception> onError)
            {
                _handler = subscription;
                _message = message;
                _onError = onError;
            }

            public object Message { get { return _message; } }
            public ISubscription Subscription { get { return _handler; } }

            public void Process()
            {
                try
                {
                    _handler.Handle(_message);
                }
                catch (Exception exception)
                {
                    _onError(this, exception);
                }
            }
        }
    }

    public class DirectBus : AbstractBus
    {
        public DirectBus(ISubscriptionManager subscriptions, string name)
            : base(subscriptions, name)
        {
        }

        protected override void PublishCore(ICollection<QueuedMessage> messages)
        {
            foreach (var message in messages)
                message.Process();
        }
    }

    public class QueuedBus : AbstractBus
    {
        private object _lock;
        private Queue<QueuedMessage> _queue;

        public QueuedBus(ISubscriptionManager subscriptions, string name)
            : base(subscriptions, name)
        {
            _lock = new object();
            _queue = new Queue<QueuedMessage>();
        }

        public bool HandleNext()
        {
            QueuedMessage message;
            lock (_lock)
            {
                if (_queue.Count == 0)
                    return false;
                message = _queue.Dequeue();
            }
            message.Process();
            return true;
        }

        protected override void PublishCore(ICollection<QueuedMessage> messages)
        {
            lock (_lock)
            {
                foreach (var message in messages)
                    _queue.Enqueue(message);
                Monitor.PulseAll(_lock);
            }
        }

        public void WaitForMessages(CancellationToken cancel)
        {
            using (cancel.Register(PulseMonitor))
            {
                lock (_lock)
                {
                    while (_queue.Count == 0 && !cancel.IsCancellationRequested)
                        Monitor.Wait(_lock);
                }
            }
        }

        private void PulseMonitor()
        {
            lock (_lock)
                Monitor.PulseAll(_lock);
        }
    }

    public class QueuedBusProcess : IDisposable
    {
        private QueuedBus _bus;
        private bool _isRunning;
        private Task _task;
        private CancellationTokenSource _cancel;

        public QueuedBusProcess(QueuedBus bus)
        {
            _bus = bus;
            _isRunning = true;
        }

        public void Start()
        {
            _isRunning = true;
            _cancel = new CancellationTokenSource();
            _task = Task.Factory.StartNew(ProcessCore, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _cancel.Cancel();
            }
        }

        public void Dispose()
        {
            Stop();
            _cancel.Dispose();
            _task.Wait();
        }

        private void ProcessCore()
        {
            while (_isRunning)
            {
                _bus.WaitForMessages(_cancel.Token);
                while (_bus.HandleNext()) ;
            }
        }
    }
}
