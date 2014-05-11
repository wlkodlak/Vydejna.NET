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
                return _attempt().ContinueWith<Task>(Finish).Unwrap();
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

    }

    public class TaskContinuationBuilder<T>
    {
        private IEnumerable<Task> _tasks;
        private bool _hasResult;

        public TaskContinuationBuilder(IEnumerable<Task> tasks, bool hasResult)
        {
            _tasks = tasks;
            _hasResult = hasResult;
        }

        public TaskContinuationBuilder<T> Catch<TException>(Func<TException, bool> handler)
        {
            return this;
        }

        public Task<T> GetTask()
        {
            return null;
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
