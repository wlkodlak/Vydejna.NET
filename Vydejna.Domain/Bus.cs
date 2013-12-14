using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
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

        public static IHandleRegistration<T> Subscribe<T>(this ISubscribable self, Func<T, Task> handler)
        {
            return self.Subscribe<T>(new DelegatedAsyncHandler<T>(handler));
        }

        public static void HandleErrorsWith<T>(this IHandleRegistration<T> self, Action<T, Exception> handler)
        {
            self.HandleErrorsWith(new DelegatedCatcher<T>(handler));
        }

        private class DelegatedSyncHandler<T> : IHandle<T>
        {
            private Action<T> _handler;

            public DelegatedSyncHandler(Action<T> handler)
            {
                _handler = handler;
            }

            public Task Handle(T message)
            {
                try
                {
                    _handler(message);
                    return TaskResult.GetCompletedTask();
                }
                catch (Exception ex)
                {
                    return TaskResult.GetFailedTask(ex);
                }
            }
        }

        private class DelegatedAsyncHandler<T> : IHandle<T>
        {
            private Func<T, Task> _handler;

            public DelegatedAsyncHandler(Func<T, Task> handler)
            {
                _handler = handler;
            }

            public Task Handle(T message)
            {
                return _handler(message);
            }
        }

        private class DelegatedCatcher<T> : ICatch<T>
        {
            private Action<T, Exception> _catcher;

            public DelegatedCatcher(Action<T, Exception> catcher)
            {
                _catcher = catcher;
            }

            public void HandleError(T message, Exception exception)
            {
                if (_catcher != null)
                    _catcher(message, exception);
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
        private ISubscriptionManager _subscriptions;

        public AbstractBus(ISubscriptionManager subscribtions)
        {
            _subscriptions = subscribtions;
        }

        public void Publish<T>(T message)
        {
            var messagesToQueue = _subscriptions.FindHandlers(message.GetType()).Select(s => new QueuedMessage(s, message)).ToList();
            PublishCore(messagesToQueue);
        }

        public IHandleRegistration<T> Subscribe<T>(IHandle<T> handler)
        {
            return _subscriptions.Register(handler);
        }

        protected abstract void PublishCore(ICollection<QueuedMessage> messages);

        protected class QueuedMessage
        {
            private object _message;
            private ISubscription _handler;

            public QueuedMessage(ISubscription subscription, object message)
            {
                _handler = subscription;
                _message = message;
            }

            public object Message { get { return _message; } }
            public ISubscription Subscription { get { return _handler; } }

            public void Process()
            {
                try
                {
                    _handler.Handle(_message).Wait();
                }
                catch (AggregateException exception)
                {
                    _handler.HandleError(_message, exception.GetBaseException());
                }
            }
        }
    }

    public class DirectBus : AbstractBus
    {
        public DirectBus(ISubscriptionManager subscriptions)
            : base(subscriptions)
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
        private ConcurrentQueue<QueuedMessage> _queue;

        public QueuedBus(ISubscriptionManager subscriptions)
            : base(subscriptions)
        {
            _queue = new ConcurrentQueue<QueuedMessage>();
        }

        public void HandleNext()
        {
            QueuedMessage message;
            if (_queue.TryDequeue(out message))
                message.Process();
        }

        protected override void PublishCore(ICollection<AbstractBus.QueuedMessage> messages)
        {
            foreach (var message in messages)
                _queue.Enqueue(message);
        }
    }
}
