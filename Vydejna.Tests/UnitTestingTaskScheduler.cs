using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Vydejna.Tests
{
    public class UnitTestingTaskScheduler : TaskScheduler, IDisposable
    {
        private object _lock = new object();
        private readonly List<Task> _waiting = new List<Task>();
        private CancellationTokenSource _cancel = new CancellationTokenSource();

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return Enumerable.Empty<Task>();
        }

        protected override void QueueTask(Task task)
        {
            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return TryExecuteTask(task);
        }

        public override int MaximumConcurrencyLevel
        {
            get { return 1; }
        }

        public static void RunTest(Action<UnitTestingTaskScheduler> test)
        {
            var scheduler = new UnitTestingTaskScheduler();
            var factory = new TaskFactory(scheduler._cancel.Token, TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);
            var testTask = factory.StartNew(new TestTask(test, scheduler).Do);
            testTask.GetAwaiter().GetResult();
        }

        private class TestTask
        {
            public Action<UnitTestingTaskScheduler> test;
            public UnitTestingTaskScheduler scheduler;
            public TestTask(Action<UnitTestingTaskScheduler> test, UnitTestingTaskScheduler scheduler)
            {
                this.test = test;
                this.scheduler = scheduler;
            }
            public void Do()
            {
                test(scheduler);
            }
        }

        public void TryToCompleteTasks(int timeout)
        {
            _cancel.CancelAfter(timeout);
        }

        void IDisposable.Dispose()
        {
            _cancel.Dispose();
        }
    }
}
