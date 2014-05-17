using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestScheduler : TaskScheduler
    {
        private object _lock;
        private TaskFactory _defaultFactory;
        private Queue<Task> _tasks;
        private List<Task> _faulted;

        public TestScheduler()
        {
            _lock = new object();
            _defaultFactory = new TaskFactory(this);
            _tasks = new Queue<Task>();
            _faulted = new List<Task>();
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            lock (_lock)
                return _tasks.ToList();
        }

        protected override void QueueTask(Task task)
        {
            lock (_lock)
                _tasks.Enqueue(task);
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
            ProcessTasks();
        }

        private bool ProcessTasks()
        {
            var anythingDone = false;
            Task task = null;
            while (true)
            {
                lock (_lock)
                {
                    if (task != null && task.Exception != null)
                        _faulted.Add(task);
                    if (_tasks.Count == 0)
                        return anythingDone;
                    task = _tasks.Dequeue();
                }
                if (TryExecuteTask(task))
                    anythingDone = true;
                else
                    task = null;
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
