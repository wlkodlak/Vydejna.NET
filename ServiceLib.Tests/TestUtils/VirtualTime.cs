using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class VirtualTime : ITime
    {
        private object _lock = new object();
        private DateTime _now = new DateTime(2000, 1, 1);
        private List<DelayedTask> _delayedTasks = new List<DelayedTask>();

        private class DelayedTask : IComparable
        {
            public DateTime Time;
            public CancellationToken Cancel;
            public TaskCompletionSource<object> Task;
            public CancellationTokenRegistration CancelRegistration;

            public DelayedTask(DateTime time, CancellationToken cancel)
            {
                Time = time;
                Cancel = cancel;
                Task = new TaskCompletionSource<object>();
                if (cancel.CanBeCanceled)
                {
                    CancelRegistration = cancel.Register(Dispose);
                }
            }

            public int CompareTo(object obj)
            {
                var oth = obj as DelayedTask;
                return oth == null ? 1 : Time.CompareTo(oth.Time);
            }

            public void Dispose()
            {
                Task.TrySetCanceled();
            }
        }

        public void SetTime(DateTime now)
        {
            IEnumerable<DelayedTask> launch;
            lock (_lock)
            {
                _now = now;
                launch = _delayedTasks.Where(t => t.Time <= _now && !t.Cancel.IsCancellationRequested).ToList();
                _delayedTasks.RemoveAll(t => t.Time <= _now || t.Cancel.IsCancellationRequested);
            }
            foreach (var task in launch)
                task.Task.TrySetResult(null);
        }

        public DateTime GetUtcTime()
        {
            return _now;
        }

        public Task Delay(int milliseconds, CancellationToken cancel)
        {
            if (milliseconds < 0)
                milliseconds = 0;
            lock (_lock)
            {
                var task = new DelayedTask(_now.AddMilliseconds(milliseconds), cancel);
                var index = _delayedTasks.BinarySearch(task);
                if (index < 0)
                    _delayedTasks.Insert(~index, task);
                else
                    _delayedTasks.Insert(index, task);
                return task.Task.Task;
            }
        }
    }
}
