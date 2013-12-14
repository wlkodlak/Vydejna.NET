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

    public class DirectBus : IBus
    {
        private ISubscriptionManager _subscriptions;

        public DirectBus(ISubscriptionManager subscribtions)
        {
            _subscriptions = subscribtions;
        }

        public void Publish<T>(T message)
        {
            var handlers = _subscriptions.FindHandlers(typeof(T));
            foreach (var handler in handlers)
                new HandlerInvocation(message, handler).Run();
        }

        public IHandleRegistration<T> Subscribe<T>(IHandle<T> handler)
        {
            return _subscriptions.Register(handler);
        }

        private class HandlerInvocation
        {
            private object _message;
            private ISubscription _handler;

            public HandlerInvocation(object message, ISubscription handler)
            {
                _message = message;
                _handler = handler;
            }
            
            public void Run()
            {
                var taskHandle = _handler.Handle(_message);
                taskHandle.ContinueWith(HandleError, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }

            private void HandleError(Task task)
            {
                var exception = task.Exception.GetBaseException();
                _handler.HandleError(_message, exception);
            }
        }
    }
}
