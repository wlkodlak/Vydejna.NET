using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Domain;

namespace Vydejna.Tests
{
    public class VirtualTime : ITime
    {
        private object _lock = new object();
        private DateTime _now = new DateTime(2000, 1, 1);
        private List<DelayedTask> _delayedTasks = new List<DelayedTask>();

        private class DelayedTask : IComparable
        {
            public DateTime Time;
            public TaskCompletionSource<object> TaskCompletion;
            public bool Cancelled;

            public DelayedTask(DateTime time)
            {
                Time = time;
                TaskCompletion = new TaskCompletionSource<object>();
            }

            public int CompareTo(object obj)
            {
                var oth = obj as DelayedTask;
                return oth == null ? 1 : Time.CompareTo(oth.Time);
            }
        }

        public void SetTime(DateTime now)
        {
            IEnumerable<DelayedTask> launch;
            lock (_lock)
            {
                _now = now;
                launch = _delayedTasks.Where(t => t.Time <= _now && !t.Cancelled).ToList();
                _delayedTasks.RemoveAll(t => t.Time <= _now || t.Cancelled);
            }
            foreach (var task in launch)
                task.TaskCompletion.TrySetResult(null);
        }

        public DateTime GetTime()
        {
            return _now;
        }

        public Task Delay(int milliseconds, CancellationToken cancel)
        {
            if (milliseconds <= 0)
                return Task.FromResult<object>(null);
            lock (_lock)
            {
                var task = new DelayedTask(_now.AddMilliseconds(milliseconds));
                var index = _delayedTasks.BinarySearch(task);
                if (index < 0)
                    _delayedTasks.Insert(~index, task);
                else
                    _delayedTasks.Insert(index, task);
                cancel.Register(() => { task.Cancelled = true; task.TaskCompletion.TrySetCanceled(); });
                return task.TaskCompletion.Task;
            }
        }
    }
}
