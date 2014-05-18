using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class TaskUtils
    {
        public static Task CompletedTask()
        {
            return TaskUtils.FromResult<object>(null);
        }

        public static Task<T> FromResult<T>(T result)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(result);
            return tcs.Task;
        }

        public static Task<T> FromError<T>(Exception exception)
        {
            var tcs = new TaskCompletionSource<T>();
            if (exception is TaskCanceledException)
                tcs.SetCanceled();
            else if (exception is AggregateException)
                tcs.SetException((exception as AggregateException).InnerExceptions);
            else
                tcs.SetException(exception);
            return tcs.Task;
        }

        public static TaskContinuationBuilder<T> FromEnumerable<T>(IEnumerable<Task> tasks)
        {
            return new TaskContinuationBuilder<T>(tasks, true);
        }

        public static TaskContinuationBuilder<object> FromEnumerable(IEnumerable<Task> tasks)
        {
            return new TaskContinuationBuilder<object>(tasks, false);
        }

        public static Task<T> CancelledTask<T>()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        public static Task<T> Retry<T>(Func<Task<T>> attempt, CancellationToken cancel = default(CancellationToken), int retries = -1)
        {
            return new RetryContext<T>(attempt, cancel, retries).RunAttempt();
        }

        private class RetryContext<T>
        {
            private Func<Task<T>> _attempt;
            private CancellationToken _cancel;
            private int _retriesLeft;

            public RetryContext(Func<Task<T>> attempt, CancellationToken cancel, int retriesLeft)
            {
                _attempt = attempt;
                _cancel = cancel;
                _retriesLeft = retriesLeft;
            }

            public Task<T> RunAttempt()
            {
                return _attempt().ContinueWith<Task<T>>(Finish).Unwrap();
            }

            private Task<T> Finish(Task<T> taskAttempt)
            {
                bool isTransientException = taskAttempt.Exception != null && taskAttempt.Exception.InnerException is TransientErrorException;
                if (!isTransientException)
                    return taskAttempt;
                else if (_cancel.IsCancellationRequested)
                    return TaskUtils.CancelledTask<T>();
                else if (_retriesLeft == -1)
                    return RunAttempt();
                else if (--_retriesLeft > 0)
                    return RunAttempt();
                else
                    return taskAttempt;
            }
        }

        public static Task Retry(Func<Task> attempt, CancellationToken cancel = default(CancellationToken), int retries = -1)
        {
            return new RetryContext(attempt, cancel, retries).RunAttempt();
        }

        private class RetryContext
        {
            private Func<Task> _attempt;
            private CancellationToken _cancel;
            private int _retriesLeft;

            public RetryContext(Func<Task> attempt, CancellationToken cancel, int retriesLeft)
            {
                _attempt = attempt;
                _cancel = cancel;
                _retriesLeft = retriesLeft;
            }

            public Task RunAttempt()
            {
                Task taskAttempt;
                try
                {
                    taskAttempt = _attempt();
                }
                catch (Exception exception)
                {
                    return TaskUtils.FromError<object>(exception);
                }
                return taskAttempt.ContinueWith<Task>(Finish).Unwrap();
            }

            private Task Finish(Task taskAttempt)
            {
                bool isTransientException = taskAttempt.Exception != null && taskAttempt.Exception.InnerException is TransientErrorException;
                if (!isTransientException)
                    return taskAttempt;
                else if (_cancel.IsCancellationRequested)
                    return TaskUtils.CancelledTask<object>();
                else if (_retriesLeft == -1)
                    return RunAttempt();
                else if (--_retriesLeft > 0)
                    return RunAttempt();
                else
                    return taskAttempt;
            }
        }

        private class DelayContext
        {
            private TaskCompletionSource<object> _tcs;
            private Timer _timer;
            private CancellationTokenRegistration _cancelHandler;
            
            public DelayContext(int milliseconds, CancellationToken cancel)
            {
                _tcs = new TaskCompletionSource<object>();
                _timer = new Timer(OnTimer, null, milliseconds, Timeout.Infinite);
                _cancelHandler = cancel.Register(OnCancel);
            }

            public Task Task { get { return _tcs.Task; } }

            private void OnTimer(object state)
            {
                _timer.Dispose();
                _cancelHandler.Dispose();
                _tcs.TrySetResult(null);
            }

            private void OnCancel()
            {
                _timer.Dispose();
                _tcs.TrySetCanceled();
            }
        }

        public static Task Delay(int milliseconds, CancellationToken cancel)
        {
            return new DelayContext(milliseconds, cancel).Task;
        }
    }

    public class TaskContinuationBuilder<T>
    {
        private IEnumerable<Task> _tasks;
        private bool _hasResult, _catchAll;
        private TaskScheduler _scheduler;
        private IEnumerator<Task> _enumerator;

        private struct CatchPair
        {
            public Type Type;
            public Predicate<Exception> Handler;
            public CatchPair(Type type, Predicate<Exception> handler)
            {
                Type = type;
                Handler = handler;
            }
        }

        public TaskContinuationBuilder(IEnumerable<Task> tasks, bool hasResult)
        {
            _tasks = tasks;
            _hasResult = hasResult;
            _scheduler = TaskScheduler.Current;
        }

        public TaskContinuationBuilder<T> CatchAll()
        {
            _catchAll = true;
            return this;
        }

        public TaskContinuationBuilder<T> UseScheduler(TaskScheduler scheduler)
        {
            _scheduler = scheduler;
            return this;
        }

        public Task<T> GetTask()
        {
            _enumerator = _tasks.GetEnumerator();
            var task = new Task<Task<T>>(ProcessingCore);
            task.Start(_scheduler);
            return task.Unwrap();
        }

        private Task<T> ProcessingCore()
        {
            return ProcessingCore(null);
        }

        private Task<T> ProcessingCore(Task previous)
        {
            try
            {
                if (_enumerator.MoveNext())
                {
                    return _enumerator.Current.ContinueWith<Task<T>>(ProcessingCore).Unwrap();
                }
                else
                {
                    _enumerator.Dispose();
                    if (_hasResult)
                    {
                        var previousTyped = previous as Task<T>;
                        if (previousTyped != null)
                            return previousTyped;
                        else if (previous == null)
                        {
                            return TaskUtils.FromError<T>(new InvalidOperationException(
                                string.Format("Enumeration yielded no tasks for Task<{0}>", typeof(T).FullName)));
                        }
                        else if (previous.IsCanceled)
                            return TaskUtils.CancelledTask<T>();
                        else if (previous.IsFaulted)
                            return TaskUtils.FromError<T>(previous.Exception);
                        else
                        {
                            return TaskUtils.FromError<T>(new InvalidOperationException(
                                string.Format("Last returned task is not a Task<{0}> (it's {1})", typeof(T).FullName, TaskType(previous.GetType()))));
                        }
                    }
                    else
                    {
                        return TaskUtils.FromResult(default(T));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_catchAll)
                    return TaskUtils.FromError<T>(ex);
                else
                    return TaskUtils.FromResult(default(T));
            }
        }

        private string TaskType(Type type)
        {
            if (!type.IsGenericType)
                return "Task";
            else
                return type.GetGenericArguments()[0].FullName;
        }
    }

    public class AutoResetEventAsync
    {
        private object _lock;
        private bool _isSet;
        private Queue<TaskCompletionSource<object>> _waiters;

        public AutoResetEventAsync()
        {
            _lock = new object();
            _isSet = false;
            _waiters = new Queue<TaskCompletionSource<object>>();
        }

        public void Set()
        {
            TaskCompletionSource<object> waiterToFinish = null;
            lock (_lock)
            {
                if (_isSet)
                    return;
                if (_waiters.Count > 0)
                    waiterToFinish = _waiters.Dequeue();
                else
                    _isSet = true;
            }
            if (waiterToFinish != null)
                waiterToFinish.SetResult(null);
        }

        public Task Wait()
        {
            lock (_lock)
            {
                if (_isSet)
                {
                    _isSet = false;
                    return TaskUtils.CompletedTask();
                }
                var waiter = new TaskCompletionSource<object>(null);
                _waiters.Enqueue(waiter);
                return waiter.Task;
            }
        }
    }
}
