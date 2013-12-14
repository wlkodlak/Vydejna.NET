using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SubscriptionManager
    {
        private UpdateLock _lock;
        private Dictionary<Type, ICollection<IHandle<object>>> _handlers;
        private ICollection<IHandle<object>> _empty;

        private class Subscription<T> : IDisposable, IHandle<object>, IHandleRegistration<T>
        {
            private SubscriptionManager _parent;
            private IHandle<T> _handler;
            private bool _enabled;
            public readonly Type Type;

            public Subscription(SubscriptionManager parent, IHandle<T> handler)
            {
                _parent = parent;
                _handler = handler;
                _enabled = true;
                Type = typeof(T);
            }

            public Task Handle(object message)
            {
                if (_enabled)
                    return _handler.Handle((T)message);
                else
                    return TaskResult.GetCompletedTask();
            }

            public void Dispose()
            {
                _parent.ChangeRegistration(Type, this, false);
            }

            public void ReplaceWith(IHandle<T> handler)
            {
                _handler = handler;
            }
        }

        public SubscriptionManager()
        {
            _lock = new UpdateLock();
            _handlers = new Dictionary<Type, ICollection<IHandle<object>>>();
            _empty = new IHandle<object>[0];
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            var subscription = new Subscription<T>(this, handler);
            ChangeRegistration(subscription.Type, subscription, true);
            return subscription;
        }

        private void ChangeRegistration(Type type, IHandle<object> subscription, bool add)
        {
            using (_lock.Update())
            {
                var list = FindHandlers(type);
                var copy = list.ToList();
                if (add)
                    copy.Add(subscription);
                else
                    copy.Remove(subscription);
                _lock.Write();
                _handlers[type] = copy;
            }
        }

        public ICollection<IHandle<object>> FindHandlers(Type type)
        {
            using (_lock.Read())
            {
                ICollection<IHandle<object>> found;
                if (_handlers.TryGetValue(type, out found))
                    return found;
                else
                    return _empty;
            }
        }

        public IEnumerable<Type> GetHandledTypes()
        {
            using (_lock.Read())
                return _handlers.Keys.ToList();
        }
    }
}
