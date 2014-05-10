using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class TaskUtils
    {
        public static Task CompletedTask()
        {
            return Task.FromResult<object>(null);
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
