using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        void Subscribe<T>(IHandle<T> handler);
    }

    public interface IBus : IPublisher, ISubscribable
    {
    }

    public class AsyncRpcMessage<TQuery, TResponse>
    {
        public readonly TQuery Query;
        public readonly TaskCompletionSource<TResponse> Response;

        public AsyncRpcMessage(TQuery query, TaskCompletionSource<TResponse> response)
        {
            Query = query;
            Response = response;
        }
    }

    public static class BusExtensions
    {
        public static void Subscribe<T>(this ISubscribable self, Action<T> handler)
        {
            self.Subscribe(new DelegatedSyncHandler<T>(handler));
        }

        private class DelegatedSyncHandler<T> : IHandle<T>
        {
            private readonly Action<T> _handler;

            public DelegatedSyncHandler(Action<T> handler)
            {
                _handler = handler;
            }

            public void Handle(T message)
            {
                _handler(message);
            }
        }

        public static void Subscribe<T>(this ISubscribable self, IProcessEvent<T> handler)
        {
            self.Subscribe(new ProcessEventToHandleAdapter<T>(handler));
        }

        private class ProcessEventToHandleAdapter<T> : IHandle<AsyncRpcMessage<T, object>>
        {
            private readonly IProcessEvent<T> _handler;

            public ProcessEventToHandleAdapter(IProcessEvent<T> handler)
            {
                _handler = handler;
            }

            public void Handle(AsyncRpcMessage<T, object> message)
            {
                _handler.Handle(message.Query).ContinueWith(task =>
                {
                    if (task.Exception != null)
                        message.Response.TrySetException(task.Exception.InnerExceptions);
                    else if (task.IsCanceled)
                        message.Response.TrySetCanceled();
                    else
                        message.Response.TrySetResult(null);
                });
            }
        }

        public static void Subscribe<T>(this ISubscribable self, IProcessCommand<T> handler)
        {
            self.Subscribe(new ProcessCommandToHandleAdapter<T>(handler));
        }

        private class ProcessCommandToHandleAdapter<T> : IHandle<AsyncRpcMessage<T, CommandResult>>
        {
            private readonly IProcessCommand<T> _handler;

            public ProcessCommandToHandleAdapter(IProcessCommand<T> handler)
            {
                _handler = handler;
            }

            public void Handle(AsyncRpcMessage<T, CommandResult> message)
            {
                _handler.Handle(message.Query).ContinueWith(task =>
                {
                    if (task.Exception != null)
                        message.Response.TrySetException(task.Exception.InnerExceptions);
                    else if (task.IsCanceled)
                        message.Response.TrySetCanceled();
                    else
                        message.Response.TrySetResult(task.Result);
                });
            }
        }

        public static void Subscribe<TQuery, TAnswer>(this ISubscribable self, IAnswer<TQuery, TAnswer> handler)
        {
            self.Subscribe(new AnswerToHandleAdapter<TQuery, TAnswer>(handler));
        }

        private class AnswerToHandleAdapter<TQuery, TAnswer> : IHandle<AsyncRpcMessage<TQuery, TAnswer>>
        {
            private readonly IAnswer<TQuery, TAnswer> _handler;

            public AnswerToHandleAdapter(IAnswer<TQuery, TAnswer> handler)
            {
                _handler = handler;
            }

            public void Handle(AsyncRpcMessage<TQuery, TAnswer> message)
            {
                _handler.Handle(message.Query).ContinueWith(task =>
                {
                    if (task.Exception != null)
                        message.Response.TrySetException(task.Exception.InnerExceptions);
                    else if (task.IsCanceled)
                        message.Response.TrySetCanceled();
                    else
                        message.Response.TrySetResult(task.Result);
                });
            }
        }

        public static Task<CommandResult> SendCommand<TCommand>(this IPublisher self, TCommand command)
        {
            return SendQuery<TCommand, CommandResult>(self, command);
        }

        public static Task SendEvent<TEvent>(this IPublisher self, TEvent command)
        {
            return SendQuery<TEvent, object>(self, command);
        }

        public static Task<TAnswer> SendQuery<TQuery, TAnswer>(this IPublisher self, TQuery query)
        {
            var tcs = new TaskCompletionSource<TAnswer>();
            self.Publish(new AsyncRpcMessage<TQuery, TAnswer>(query, tcs));
            return tcs.Task;
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
        private readonly ISubscriptionManager _subscriptions;

        public AbstractBus(ISubscriptionManager subscribtions, string name)
        {
            _name = name ?? "Bus";
            _subscriptions = subscribtions;
        }

        public void Publish<T>(T message)
        {
            var messagesToQueue = _subscriptions.FindHandlers(message.GetType()).Select(s => new QueuedMessage(s, message, OnError)).ToList();
            PublishCore(messagesToQueue);
        }

        public void Subscribe<T>(IHandle<T> handler)
        {
            _subscriptions.Register(handler);
        }

        protected abstract void PublishCore(ICollection<QueuedMessage> messages);

        protected virtual void OnError(QueuedMessage message, Exception exception)
        {
            Trace.TraceError("When handling {0} exception occurred: {1}", message.Message.GetType().Name, exception);
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
            QueuedMessage message = null;
            lock (_lock)
            {
                while (message == null)
                {
                    if (_queue.Count != 0)
                        message = _queue.Dequeue();
                    else
                        return false;
                }
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

    public class QueuedBusProcess : IProcessWorker
    {
        private QueuedBus _bus;
        private Task[] _tasks;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private CancellationTokenSource _cancel;
        private int _workerCount;
        private TaskScheduler _scheduler;

        public QueuedBusProcess(QueuedBus bus)
        {
            _bus = bus;
            _workerCount = Environment.ProcessorCount * 2;
        }

        public QueuedBusProcess WithWorkers(int workers)
        {
            _workerCount = Math.Min(Math.Max(workers, 1), 32);
            return this;
        }

        public void Start()
        {
            State = ProcessState.Running;
            _cancel = new CancellationTokenSource();
            _tasks = new Task[_workerCount];
            for (int i = 0; i < _workerCount; i++)
            {
                _tasks[i] = new Task(ProcessCore, TaskCreationOptions.LongRunning);
                _tasks[i].Start(_scheduler);
            }
        }

        public void Pause()
        {
            State = ProcessState.Pausing;
            _cancel.Cancel();
        }

        public void Stop()
        {
            State = ProcessState.Stopping;
            _cancel.Cancel();
        }

        public void Dispose()
        {
            State = ProcessState.Stopping;
            if (_cancel != null)
                _cancel.Cancel();
            Task.WaitAll(_tasks);
        }

        private void ProcessCore()
        {
            while (_processState == ProcessState.Running)
            {
                _bus.WaitForMessages(_cancel.Token);
                while (_bus.HandleNext()) ;
            }
        }

        public ProcessState State
        {
            get { return _processState; }
            private set
            {
                _processState = value;
                if (_onStateChanged != null)
                    _onStateChanged(_processState);
            }
        }

        public void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler)
        {
            _onStateChanged = onStateChanged;
            _scheduler = scheduler;
            State = ProcessState.Inactive;
        }

    }
}
