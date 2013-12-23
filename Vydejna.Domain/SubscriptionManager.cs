using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface ISubscriptionManager
    {
        IHandleRegistration<T> Register<T>(IHandle<T> handler);
        IEnumerable<Type> GetHandledTypes();
        ICollection<ISubscription> FindHandlers(Type type);
    }
    
    public class SubscriptionManager : ISubscriptionManager
    {
        private UpdateLock _lock;
        private Dictionary<Type, ICollection<ISubscription>> _handlers;
        private ICollection<ISubscription> _empty;

        private class Subscription<T> : ISubscription<T>
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

            public void Handle(object message)
            {
                if (_enabled)
                    _handler.Handle((T)message);
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
            _handlers = new Dictionary<Type, ICollection<ISubscription>>();
            _empty = new ISubscription[0];
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            var subscription = new Subscription<T>(this, handler);
            ChangeRegistration(subscription.Type, subscription, true);
            return subscription;
        }

        private void ChangeRegistration(Type type, ISubscription subscription, bool add)
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

        public ICollection<ISubscription> FindHandlers(Type type)
        {
            using (_lock.Read())
            {
                ICollection<ISubscription> found;
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
