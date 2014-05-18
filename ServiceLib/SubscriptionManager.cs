using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface ISubscriptionManager
    {
        void Register<T>(IHandle<T> handler);
        IEnumerable<Type> GetHandledTypes();
        ICollection<ISubscription> FindHandlers(Type type);
    }
    public interface ICommandSubscriptionManager
    {
        void Register<T>(IProcess<T> handler);
        IEnumerable<Type> GetHandledTypes();
        ICommandSubscription FindHandler(Type type);
    }
    public interface ISubscribeToCommandManager
    {
        void Subscribe(ICommandSubscriptionManager mgr);
    }

    public class SubscriptionManager : ISubscriptionManager
    {
        private ReaderWriterLockSlim _lock;
        private Dictionary<Type, ICollection<ISubscription>> _handlers;
        private ICollection<ISubscription> _empty;

        private class Subscription<T> : ISubscription
        {
            private IHandle<T> _handler;

            public Subscription(IHandle<T> handler)
            {
                _handler = handler;
            }

            public void Handle(object message)
            {
                _handler.Handle((T)message);
            }
        }

        public SubscriptionManager()
        {
            _lock = new ReaderWriterLockSlim();
            _handlers = new Dictionary<Type, ICollection<ISubscription>>();
            _empty = new ISubscription[0];
        }

        public void Register<T>(IHandle<T> handler)
        {
            _lock.EnterWriteLock();
            try
            {
                var type = typeof(T);
                var list = FindHandlersInternal(type);
                var copy = list.ToList();
                copy.Add(new Subscription<T>(handler));
                _handlers[type] = copy;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public ICollection<ISubscription> FindHandlers(Type type)
        {
            _lock.EnterReadLock();
            try
            {
                return FindHandlersInternal(type);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private ICollection<ISubscription> FindHandlersInternal(Type type)
        {
            ICollection<ISubscription> found;
            if (_handlers.TryGetValue(type, out found))
                return found;
            else
                return _empty;
        }

        public IEnumerable<Type> GetHandledTypes()
        {
            _lock.EnterReadLock();
            try
            {
                return _handlers.Keys.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public class CommandSubscriptionManager : ICommandSubscriptionManager
    {
        private ReaderWriterLockSlim _lock;
        private Dictionary<Type, ICommandSubscription> _handlers;

        private class Subscription<T> : ICommandSubscription
        {
            private IProcess<T> _handler;

            public Subscription(IProcess<T> handler)
            {
                _handler = handler;
            }

            public Task Handle(object command)
            {
                return _handler.Handle((T)command);
            }
        }

        public CommandSubscriptionManager()
        {
            _lock = new ReaderWriterLockSlim();
            _handlers = new Dictionary<Type, ICommandSubscription>();
        }

        public void Register<T>(IProcess<T> handler)
        {
            _lock.EnterWriteLock();
            try
            {
                var type = typeof(T);
                if (_handlers.ContainsKey(type))
                    throw new InvalidOperationException(string.Format("Type {0} is already registered", type.Name));
                _handlers.Add(type, new Subscription<T>(handler));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public ICommandSubscription FindHandler(Type type)
        {
            _lock.EnterReadLock();
            try
            {
                ICommandSubscription found;
                _handlers.TryGetValue(type, out found);
                return found;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IEnumerable<Type> GetHandledTypes()
        {
            _lock.EnterReadLock();
            try
            {
                return _handlers.Keys.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
}
