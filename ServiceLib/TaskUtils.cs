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

        public static Task<T> Retry<T>(Func<Task<T>> attempt, ITime time, CancellationToken cancel = default(CancellationToken), int retries = -1)
        {
            return new RetryContext<T>(attempt, time, cancel, retries).RunAttempt();
        }

        private class RetryContext<T>
        {
            private Func<Task<T>> _attempt;
            private CancellationToken _cancel;
            private int _retriesLeft, _retryAttempt;
            private ITime _time;

            public RetryContext(Func<Task<T>> attempt, ITime time, CancellationToken cancel, int retriesLeft)
            {
                _attempt = attempt;
                _cancel = cancel;
                _retriesLeft = retriesLeft;
                _time = time;
            }

            public Task<T> RunAttempt()
            {
                Task<T> taskAttempt;
                try
                {
                    taskAttempt = _attempt();
                }
                catch (Exception exception)
                {
                    return TaskUtils.FromError<T>(exception);
                }
                return taskAttempt.ContinueWith<Task<T>>(Finish).Unwrap();
            }

            private Task<T> Finish(Task<T> taskAttempt)
            {
                bool isTransientException = taskAttempt.Exception != null && taskAttempt.Exception.InnerException is TransientErrorException;
                if (!isTransientException)
                    return taskAttempt;
                else if (_cancel.IsCancellationRequested)
                    return TaskUtils.CancelledTask<T>();
                else if (_retriesLeft != -1 && _retriesLeft == 0)
                    return taskAttempt;
                else
                    return RunNextAttempt();
            }

            private Task<T> RunNextAttempt()
            {
                if (_retriesLeft > 0)
                    _retriesLeft--;
                _retryAttempt++;
                if (_retryAttempt <= 1)
                    return RunAttempt();
                else
                    return _time.Delay(AttemptDelay(_retryAttempt), _cancel).ContinueWith<Task<T>>(RunNextAttempt).Unwrap();
            }

            private Task<T> RunNextAttempt(Task delayTask)
            {
                if (delayTask.IsCanceled)
                    return TaskUtils.CancelledTask<T>();
                else
                    return RunAttempt();
            }

            private static int AttemptDelay(int attempt)
            {
                switch (attempt)
                {
                    case 1:
                        return 0;
                    case 2:
                        return 50;
                    case 3:
                        return 200;
                    case 4:
                        return 500;
                    case 5:
                        return 2000;
                    default:
                        return 5000;
                }
            }
        }

        public static Task Retry(Func<Task> attempt, ITime time, CancellationToken cancel = default(CancellationToken), int retries = -1)
        {
            return new RetryContext(attempt, time, cancel, retries).RunAttempt();
        }

        private class RetryContext
        {
            private Func<Task> _attempt;
            private CancellationToken _cancel;
            private int _retriesLeft, _retryAttempt;
            private ITime _time;

            public RetryContext(Func<Task> attempt, ITime time, CancellationToken cancel, int retriesLeft)
            {
                _attempt = attempt;
                _time = time;
                _cancel = cancel;
                _retriesLeft = retriesLeft;
                _retryAttempt = 0;
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
                else if (_retriesLeft != -1 && _retriesLeft == 0)
                    return taskAttempt;
                else
                    return RunNextAttempt();
            }

            private Task RunNextAttempt()
            {
                if (_retriesLeft > 0)
                    _retriesLeft--;
                _retryAttempt++;
                if (_retryAttempt <= 1)
                    return RunAttempt();
                else
                    return _time.Delay(AttemptDelay(_retryAttempt), _cancel).ContinueWith<Task>(RunNextAttempt).Unwrap();
            }

            private Task RunNextAttempt(Task delayTask)
            {
                if (delayTask.IsCanceled)
                    return delayTask;
                else
                    return RunAttempt();
            }

            private static int AttemptDelay(int attempt)
            {
                switch (attempt)
                {
                    case 1:
                        return 0;
                    case 2:
                        return 50;
                    case 3:
                        return 200;
                    case 4:
                        return 500;
                    case 5:
                        return 2000;
                    default:
                        return 5000;
                }
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
                        if (_catchAll || previous == null)
                            return TaskUtils.FromResult(default(T));
                        else if (previous.IsCanceled)
                            return TaskUtils.CancelledTask<T>();
                        else if (previous.Exception == null)
                            return TaskUtils.FromResult(default(T));
                        else
                            return TaskUtils.FromError<T>(previous.Exception);
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

    public class CircuitBreaker
    {
        private object _lock;
        private int _failures, _failureLimit, _retryTime;
        private DateTime _nextAttempt;
        private ITime _time;
        private CircuitBreakerState _state;
        private enum CircuitBreakerState
        {
            Failing, Working, SingleAttemptIdle, SingleAttemptBusy
        }

        public CircuitBreaker(ITime time)
        {
            _lock = new object();
            _state = CircuitBreakerState.Working;
            _nextAttempt = DateTime.MinValue;
            _time = time;
            SetupLimit(4, 15000);
        }

        public CircuitBreaker StartHalfOpen()
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Working)
                    _state = CircuitBreakerState.SingleAttemptIdle;
            }
            return this;
        }

        public CircuitBreaker SetupLimit(int failureLimit, int retryTime)
        {
            lock (_lock)
            {
                _failureLimit = Math.Max(1, failureLimit);
                _retryTime = Math.Max(5, Math.Min(60000, retryTime));
            }
            return this;
        }

        public void Execute<T>(Action<T> action, T arg)
        {
            lock (_lock)
            {
                var retry = true;
                while (retry)
                {
                    retry = false;
                    if (_state == CircuitBreakerState.Failing)
                    {
                        if (_time.GetUtcTime() < _nextAttempt)
                            throw new TransientErrorException("BREAKER", "There is lasting transient error, failing quickly");
                        else
                            _state = CircuitBreakerState.SingleAttemptBusy;
                    }
                    else if (_state == CircuitBreakerState.SingleAttemptBusy)
                    {
                        Monitor.Wait(_lock);
                        retry = true;
                    }
                    else if (_state == CircuitBreakerState.SingleAttemptIdle)
                    {
                        _state = CircuitBreakerState.SingleAttemptBusy;
                    }
                }
            }
            try
            {
                action(arg);
                lock (_lock)
                {
                    _failures = 0;
                    if (_state == CircuitBreakerState.SingleAttemptBusy)
                    {
                        _state = CircuitBreakerState.Working;
                        Monitor.PulseAll(_lock);
                    }
                }
            }
            catch
            {
                lock (_lock)
                {
                    if (_state == CircuitBreakerState.Working)
                    {
                        _failures++;
                        if (_failures > _failureLimit)
                        {
                            _state = CircuitBreakerState.Failing;
                            _nextAttempt = _time.GetUtcTime().AddMilliseconds(_retryTime);
                        }
                    }
                    else if (_state == CircuitBreakerState.SingleAttemptBusy)
                    {
                        _state = CircuitBreakerState.Failing;
                        _nextAttempt = _time.GetUtcTime().AddMilliseconds(_retryTime);
                        Monitor.PulseAll(_lock);
                    }
                }
                throw;
            }
        }
    }
}
