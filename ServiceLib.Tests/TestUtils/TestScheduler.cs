using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestScheduler : TaskScheduler
    {
        private object _lock;
        private TaskFactory _defaultFactory;
        private Queue<Task> _tasks;
        private List<Task> _faulted;
        private HashSet<Task> _pendingTasks;
        private bool _allowWaiting;
        private int _waitCycles, _waitMs;

        public TestScheduler()
        {
            _lock = new object();
            _defaultFactory = new TaskFactory(this);
            _tasks = new Queue<Task>();
            _faulted = new List<Task>();
            _pendingTasks = new HashSet<Task>();
            _waitCycles = 2;
            _waitMs = 20;
        }

        public TestScheduler AllowWaiting(int waitCycles = -1, int waitMs = -1)
        {
            _allowWaiting = true;
            if (waitCycles > 0)
                _waitCycles = waitCycles;
            if (waitMs > 0)
                _waitMs = waitMs;
            return this;
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            lock (_lock)
                return _tasks.ToList();
        }

        protected override void QueueTask(Task task)
        {
            lock (_lock)
            {
                _tasks.Enqueue(task);
                Monitor.PulseAll(_lock);
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (!base.TryExecuteTask(task))
                return false;
            lock (_lock)
            {
                if (task.Exception != null)
                    _faulted.Add(task);
            }
            return true;
        }

        public void Process()
        {
            var attemptsLeft = 5;
            while (attemptsLeft > 0)
            {
                ProcessTasks();
                lock (_lock)
                {
                    foreach (var task in _pendingTasks.ToList())
                    {
                        if (task.IsCompleted)
                            _pendingTasks.Remove(task);
                    }
                    if (_pendingTasks.Count == 0 || !_allowWaiting)
                        attemptsLeft = 0;
                    else if (!Monitor.Wait(_lock, 50))
                        attemptsLeft--;
                }
            }


        }

        private bool ProcessTasks()
        {
            var anythingDone = false;
            Task task = null;
            for (var i = 0; i < 1000; i++)
            {
                lock (_lock)
                {
                    FinishPreviousTask(task);
                    if (_tasks.Count == 0)
                        return anythingDone;
                    task = _tasks.Dequeue();
                }
                if (TryExecuteTask(task))
                    anythingDone = true;
                else
                    task = null;
            }
            throw new InvalidOperationException("Too many tasks processed - there's probably a endless loop");
        }

        private void FinishPreviousTask(Task task)
        {
            if (task != null)
            {
                if (task.Exception != null)
                    _faulted.Add(task);
                else if (!task.IsCanceled)
                {
                    var taskType = task.GetType();
                    if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var pending = taskType.GetProperty("Result").GetValue(task, null) as Task;
                        if (pending != null)
                            _pendingTasks.Add(pending);
                    }
                }
            }

        }

        public Task<TResult> Run<TResult>(Func<Task<TResult>> action, bool mustComplete = true)
        {
            var outerTask = new Task<Task<TResult>>(action);
            outerTask.Start(this);
            Process();
            if (!outerTask.IsCompleted)
                return TaskUtils.FromError<TResult>(new Exception("Outer task didn't finish - there's something wrong with TestScheduler"));
            var innerTask = outerTask.Result;
            if (!innerTask.IsCompleted && mustComplete)
                return TaskUtils.FromError<TResult>(new Exception("Task didn't finish in time"));
            return innerTask;
        }

        public Task Run(Func<Task> action, bool mustComplete = true)
        {
            var outerTask = new Task<Task>(action);
            outerTask.Start(this);
            Process();
            if (!outerTask.IsCompleted)
                return TaskUtils.FromError<object>(new Exception("Outer task didn't finish - there's something wrong with TestScheduler"));
            var innerTask = outerTask.Result;
            if (!innerTask.IsCompleted && mustComplete)
                return TaskUtils.FromError<object>(new Exception("Task didn't finish in time"));
            return innerTask;
        }

        public void RunSync(Action action)
        {
            var outerTask = new Task(action);
            outerTask.RunSynchronously(this);
        }

        public TaskFactory Factory { get { return _defaultFactory; } }
    }
}
