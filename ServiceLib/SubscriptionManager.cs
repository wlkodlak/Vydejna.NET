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
        void Register<T>(IProcessCommand<T> handler);
        IEnumerable<Type> GetHandledTypes();
        ICommandSubscription FindHandler(Type type);
    }

    public interface ISubscribeToCommandManager
    {
        void Subscribe(ICommandSubscriptionManager mgr);
    }

    public interface IEventSubscriptionManager
    {
        void Register<T>(IProcessEvent<T> handler);
        IEnumerable<Type> GetHandledTypes();
        IEventSubscription FindHandler(Type type);
    }

    public interface ISubscribeToEventManager
    {
        void Subscribe(IEventSubscriptionManager mgr);
    }

    public class SubscriptionManager : ISubscriptionManager
    {
        private readonly ReaderWriterLockSlim _lock;
        private readonly Dictionary<Type, ICollection<ISubscription>> _handlers;
        private readonly ICollection<ISubscription> _empty;

        private class Subscription<T> : ISubscription
        {
            private readonly IHandle<T> _handler;

            public Subscription(IHandle<T> handler)
            {
                _handler = handler;
            }

            public void Handle(object message)
            {
                _handler.Handle((T) message);
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
                var type = typeof (T);
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
        private readonly ReaderWriterLockSlim _lock;
        private readonly Dictionary<Type, ICommandSubscription> _handlers;

        private class Subscription<T> : ICommandSubscription
        {
            private readonly IProcessCommand<T> _handler;

            public Subscription(IProcessCommand<T> handler)
            {
                _handler = handler;
            }

            public Task<CommandResult> Handle(object command)
            {
                return _handler.Handle((T) command);
            }
        }

        public CommandSubscriptionManager()
        {
            _lock = new ReaderWriterLockSlim();
            _handlers = new Dictionary<Type, ICommandSubscription>();
        }

        public void Register<T>(IProcessCommand<T> handler)
        {
            _lock.EnterWriteLock();
            try
            {
                var type = typeof (T);
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

    public class EventSubscriptionManager : IEventSubscriptionManager
    {
        private readonly ReaderWriterLockSlim _lock;
        private readonly Dictionary<Type, IEventSubscription> _handlers;

        private class Subscription<T> : IEventSubscription
        {
            private readonly IProcessEvent<T> _handler;

            public Subscription(IProcessEvent<T> handler)
            {
                _handler = handler;
            }

            public Task Handle(object evnt)
            {
                return _handler.Handle((T) evnt);
            }
        }

        public EventSubscriptionManager()
        {
            _lock = new ReaderWriterLockSlim();
            _handlers = new Dictionary<Type, IEventSubscription>();
        }

        public void Register<T>(IProcessEvent<T> handler)
        {
            _lock.EnterWriteLock();
            try
            {
                var type = typeof (T);
                if (_handlers.ContainsKey(type))
                    throw new InvalidOperationException(string.Format("Type {0} is already registered", type.Name));
                _handlers.Add(type, new Subscription<T>(handler));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEventSubscription FindHandler(Type type)
        {
            _lock.EnterReadLock();
            try
            {
                IEventSubscription found;
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