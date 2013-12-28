using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
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
    public class QueuedExecutionDispatcher<T> : IQueuedExecutionDispatcher
    {
        private IHandle<T> _handler;
        private T _message;
        public QueuedExecutionDispatcher(IHandle<T> handler, T message)
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

    public interface IQueueExecution
    {
        void Enqueue(IQueuedExecutionDispatcher handler);
        void Enqueue<T>(IHandle<T> handler, T message);
    }

    public class QueuedExecutionWorker : IHandle<QueuedExecution>, IQueueExecution
    {
        private IBus _bus;

        public void Subscribe(IBus bus)
        {
            _bus = bus;
            bus.Subscribe<QueuedExecution>(this);
        }

        public void Handle(QueuedExecution message)
        {
            message.Dispatcher.Execute();
        }

        public void Enqueue<T>(IHandle<T> handler, T message)
        {
            Enqueue(new QueuedExecutionDispatcher<T>(handler, message));
        }

        public void Enqueue(IQueuedExecutionDispatcher handler)
        {
            _bus.Publish(new QueuedExecution(handler));
        }
    }

    public static class QueueExecutionExtensions
    {
        public static void Enqueue(this IQueueExecution self, Action action)
        {
            self.Enqueue(new QueueExecutionDispatchAction(action));
        }
        public static void Enqueue(this IQueueExecution self, Action<Exception> onError, Exception exception)
        {
            self.Enqueue(new QueueExecutionDispatchError(onError, exception));
        }
    }
}
