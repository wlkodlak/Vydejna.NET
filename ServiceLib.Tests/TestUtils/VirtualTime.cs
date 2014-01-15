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

        private class DelayedTask : IComparable, IDisposable
        {
            public DateTime Time;
            public Action TaskCompletion;
            public bool Cancelled;

            public DelayedTask(DateTime time, Action onComplete)
            {
                Time = time;
                TaskCompletion = onComplete;
            }

            public int CompareTo(object obj)
            {
                var oth = obj as DelayedTask;
                return oth == null ? 1 : Time.CompareTo(oth.Time);
            }

            public void Dispose()
            {
                Cancelled = true;
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
                task.TaskCompletion();
        }

        public DateTime GetUtcTime()
        {
            return _now;
        }

        public IDisposable Delay(int milliseconds, Action onTimer)
        {
            if (milliseconds < 0)
                milliseconds = 0;
            lock (_lock)
            {
                var task = new DelayedTask(_now.AddMilliseconds(milliseconds), onTimer);
                var index = _delayedTasks.BinarySearch(task);
                if (index < 0)
                    _delayedTasks.Insert(~index, task);
                else
                    _delayedTasks.Insert(index, task);
                return task;
            }
        }
    }
}
