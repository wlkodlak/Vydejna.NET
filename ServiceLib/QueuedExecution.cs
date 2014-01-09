using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class QueuedExecution
    {
        public IQueuedExecutionDispatcher Dispatcher { get; set; }
        public QueuedExecution(IQueuedExecutionDispatcher dispatcher)
        {
            Dispatcher = dispatcher;
        }
    }
    public interface IQueuedExecutionDispatcher
    {
        void Execute();
    }
    public class QueuedExecutionDispatchGeneric<T> : IQueuedExecutionDispatcher
    {
        private IHandle<T> _handler;
        private T _message;
        public QueuedExecutionDispatchGeneric(IHandle<T> handler, T message)
        {
            _handler = handler;
            _message = message;
        }
        public void Execute()
        {
            _handler.Handle(_message);
        }
    }
    public class QueueExecutionDispatchAction : IQueuedExecutionDispatcher
    {
        private Action _action;
        public QueueExecutionDispatchAction(Action action) { _action = action; }
        public void Execute() { _action(); }
    }
    public class QueueExecutionDispatchError : IQueuedExecutionDispatcher
    {
        private Action<Exception> _onError;
        private Exception _exception;
        public QueueExecutionDispatchError(Action<Exception> onError, Exception exception)
        {
            _onError = onError;
            _exception = exception;
        }
        public void Execute() { _onError(_exception); }
    }

    public interface IQueueExecution : IAttachBusyProcess
    {
        void Enqueue(IQueuedExecutionDispatcher handler);
    }

    public class QueuedExecutionWorker : IHandle<QueuedExecution>, IQueueExecution
    {
        private IBus _bus;

        public QueuedExecutionWorker(IBus bus)
        {
            _bus = bus;
        }

        public void Subscribe(IBus bus)
        {
            bus.Subscribe<QueuedExecution>(this);
        }

        public void Handle(QueuedExecution message)
        {
            message.Dispatcher.Execute();
        }

        public void Enqueue(IQueuedExecutionDispatcher handler)
        {
            _bus.Publish(new QueuedExecution(handler));
        }

        public IDisposable AttachBusyProcess()
        {
            return _bus.AttachBusyProcess();
        }
    }

    public static class QueueExecutionExtensions
    {
        public static void Enqueue<T>(this IQueueExecution self, IHandle<T> handler, T message)
        {
            self.Enqueue(new QueuedExecutionDispatchGeneric<T>(handler, message));
        }
        public static void Enqueue(this IQueueExecution self, Action action)
        {
            self.Enqueue(new QueueExecutionDispatchAction(action));
        }
        public static void Enqueue(this IQueueExecution self, Action<Exception> onError, Exception exception)
        {
            self.Enqueue(new QueueExecutionDispatchError(onError, exception));
        }
    }

    public class QueuedCommandSubscriptionManager : ICommandSubscriptionManager
    {
        private ICommandSubscriptionManager _manager;
        private IQueueExecution _executor;

        private class QueueingSubscription : ICommandSubscription, IHandle<CommandExecution<object>>
        {
            private IQueueExecution _executor;
            private ICommandSubscription _original;

            public QueueingSubscription(IQueueExecution executor, ICommandSubscription original)
            {
                _executor = executor;
                _original = original;
            }
            public void Handle(object command, Action onComplete, Action<Exception> onError)
            {
                _executor.Enqueue(this, new CommandExecution<object>(command, onComplete, onError));
            }
            public void Handle(CommandExecution<object> msg)
            {
                _original.Handle(msg.Command, msg.OnCompleted, msg.OnError);
            }
        }

        public QueuedCommandSubscriptionManager(ICommandSubscriptionManager manager, IQueueExecution executor)
        {
            _manager = manager;
            _executor = executor;
        }

        public IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler)
        {
            return _manager.Register(handler);
        }

        public IEnumerable<Type> GetHandledTypes()
        {
            return _manager.GetHandledTypes();
        }

        public ICommandSubscription FindHandler(Type type)
        {
            var handler =  _manager.FindHandler(type);
            if (handler == null)
                return null;
            return new QueueingSubscription(_executor, handler);
        }
    }
}
