using System;
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
        Task Publish<T>(T message);
    }

    public interface ISubscribable
    {
        IDisposable Subscribe<T>(IHandle<T> handler);
    }

    public interface IBus : IPublisher, ISubscribable
    {
    }

    public static class BusExtensions
    {
        public static IDisposable Subscribe<T>(this ISubscribable self, Action<T> handler)
        {
            return self.Subscribe<T>(new DelegatedSyncHandler<T>(handler));
        }

        public static IDisposable Subscribe<T>(this ISubscribable self, Func<T, Task> handler)
        {
            return self.Subscribe<T>(new DelegatedAsyncHandler<T>(handler));
        }

        private class DelegatedSyncHandler<T> : IHandle<T>
        {
            private Action<T> handler;

            public DelegatedSyncHandler(Action<T> handler)
            {
                this.handler = handler;
            }

            public Task Handle(T message)
            {
                try
                {
                    handler(message);
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
            private Func<T, Task> handler;

            public DelegatedAsyncHandler(Func<T, Task> handler)
            {
                this.handler = handler;
            }

            public Task Handle(T message)
            {
                return handler(message);
            }
        }
    }

    public static class SystemEvents
    {
        public class SystemInit { }
        public class SystemStarted { }
        public class SystemShutdown { }
    }

    public class DirectBus : IBus
    {
        private UpdateLock _lock;
        private Dictionary<Type, List<Subscription>> _subscriptions;

        public DirectBus()
        {
            _lock = new UpdateLock();
            _subscriptions = new Dictionary<Type, List<Subscription>>();
        }

        private class Subscription : IDisposable
        {
            private DirectBus _parent;
            private Type _type;
            private Func<object, Task> _handler;

            public Subscription(DirectBus parent, Type type, Func<object, Task> handler)
            {
                this._parent = parent;
                this._type = type;
                this._handler = handler;
            }

            public void Dispose()
            {
                using (_parent._lock.Update())
                    _parent._subscriptions[_type].Remove(this);
            }

            public Task Handle(object message)
            {
                return _handler(message);
            }
        }

        public async Task Publish<T>(T message)
        {
            List<Subscription> subscriptions;
            using (_lock.Read())
            {
                if (!_subscriptions.TryGetValue(typeof(T), out subscriptions))
                    return;
                subscriptions = subscriptions.ToList();
            }
            foreach (var item in subscriptions)
                await item.Handle(message);
        }

        public IDisposable Subscribe<T>(IHandle<T> handler)
        {
            using (_lock.Update())
            {
                var subscription = new Subscription(this, typeof(T), CreateHandler(handler));
                _lock.Write();
                List<Subscription> subscriptions;
                if (!_subscriptions.TryGetValue(typeof(T), out subscriptions))
                    _subscriptions[typeof(T)] = subscriptions = new List<Subscription>();
                subscriptions.Add(subscription);
                return subscription;
            }
        }

        private static Func<object, Task> CreateHandler<T>(IHandle<T> handler)
        {
            // return o => handler.Handle((T)o);
            var param = Expression.Parameter(typeof(object), "o");
            var cast = Expression.Convert(param, typeof(T));
            var handleMethod = typeof(IHandle<T>).GetMethod("Handle");
            var invoke = Expression.Call(Expression.Constant(handler), handleMethod, cast);
            var name = "Handle_" + typeof(T).Name;
            return Expression.Lambda<Func<object, Task>>(invoke, name, new[] { param }).Compile();
        }
    }

    public class QueuedBus : IBus
    {
        private object _lockSubscriptions;
        private object _lockQueue;
        private Dictionary<Type, List<Subscription>> _subscriptions;
        private Queue<QueuedMessage> _queue;

        public QueuedBus()
        {
            _lockSubscriptions = new object();
            _lockQueue = new object();
            _subscriptions = new Dictionary<Type, List<Subscription>>();
            _queue = new Queue<QueuedMessage>();
        }

        private class Subscription : IDisposable
        {
            private QueuedBus _parent;
            private Type _type;
            private Func<object, Task> _handler;

            public Subscription(QueuedBus parent, Type type, Func<object, Task> handler)
            {
                this._parent = parent;
                this._type = type;
                this._handler = handler;
            }

            public void Dispose()
            {
                lock (_parent._subscriptions)
                    _parent._subscriptions[_type].Remove(this);
            }

            public Task Handle(object message)
            {
                return _handler(message);
            }
        }

        private class QueuedMessage
        {
            public Subscription Subscription;
            public object Message;
        }

        public Task Publish<T>(T message)
        {
            List<QueuedMessage> messagesForQueue = null;
            lock (_lockSubscriptions)
            {
                List<Subscription> subscriptions;
                if (_subscriptions.TryGetValue(typeof(T), out subscriptions))
                    messagesForQueue = subscriptions.Select(item => new QueuedMessage { Subscription = item, Message = message }).ToList();
            }
            if (messagesForQueue != null)
            {
                lock (_lockQueue)
                {
                    messagesForQueue.ForEach(_queue.Enqueue);
                    Monitor.PulseAll(_lockQueue);
                }
            }
            return TaskResult.GetCompletedTask();
        }

        public IDisposable Subscribe<T>(IHandle<T> handler)
        {
            lock (_lockSubscriptions)
            {
                var subscription = new Subscription(this, typeof(T), CreateHandler(handler));
                List<Subscription> subscriptions;
                if (!_subscriptions.TryGetValue(typeof(T), out subscriptions))
                    _subscriptions[typeof(T)] = subscriptions = new List<Subscription>();
                subscriptions.Add(subscription);
                return subscription;
            }
        }

        public Task ProcessNext(CancellationToken cancel)
        {
            QueuedMessage message = null;
            cancel.Register(() => Monitor.PulseAll(_lockQueue));
            lock (_lockQueue)
            {
                while (_queue.Count == 0)
                {
                    if (cancel.IsCancellationRequested)
                        return TaskResult.GetCancelledTask();
                    Monitor.Wait(_lockQueue);
                }
                message = _queue.Dequeue();
            }
            return message.Subscription.Handle(message.Message);
        }

        private static Func<object, Task> CreateHandler<T>(IHandle<T> handler)
        {
            // return o => handler.Handle((T)o);
            var param = Expression.Parameter(typeof(object), "o");
            var cast = Expression.Convert(param, typeof(T));
            var handleMethod = typeof(IHandle<T>).GetMethod("Handle");
            var invoke = Expression.Call(Expression.Constant(handler), handleMethod, cast);
            var name = "Handle_" + typeof(T).Name;
            return Expression.Lambda<Func<object, Task>>(invoke, name, new[] { param }).Compile();
        }
    }
}
