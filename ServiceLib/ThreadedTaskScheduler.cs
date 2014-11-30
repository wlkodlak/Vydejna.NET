using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ThreadedTaskScheduler : TaskScheduler
    {
        private Queue<Task> _tasks;

        public ThreadedTaskScheduler()
        {
            _tasks = new Queue<Task>();
            for (int i = 0; i < 10; i++)
            {
                var thread = new Thread(ThreadFunc);
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void ThreadFunc()
        {
            while (true)
            {
                Task task;
                lock (_tasks)
                {
                    while (_tasks.Count == 0)
                        Monitor.Wait(_tasks);
                    task = _tasks.Dequeue();
                }
                TryExecuteTask(task);
            }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            lock (_tasks)
                return _tasks.ToList();
        }

        protected override void QueueTask(Task task)
        {
            if (task == null)
                throw new ArgumentNullException("task");
            lock (_tasks)
            {
                _tasks.Enqueue(task);
                Monitor.Pulse(_tasks);
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return TryExecuteTask(task);
        }
    }
}
