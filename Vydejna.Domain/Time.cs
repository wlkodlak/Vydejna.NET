using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface ITime
    {
        DateTime GetTime();
        Task Delay(int milliseconds);
    }

    public class RealTime : ITime
    {
        public DateTime GetTime()
        {
            return DateTime.Now;
        }

        public Task Delay(int milliseconds)
        {
            return Task.Delay(milliseconds);
        }
    }

    public class VirtualTime : ITime
    {
        private object _lock = new object();
        private DateTime _now = new DateTime(2000, 1, 1);
        private List<DelayedTask> _delayedTasks = new List<DelayedTask>();

        private class DelayedTask : IComparable
        {
            public DateTime Time;
            public TaskCompletionSource<object> TaskCompletion;

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
                launch = _delayedTasks.Where(t => t.Time <= _now).ToList();
                _delayedTasks.RemoveAll(t => t.Time <= _now);
            }
            foreach (var task in launch)
                task.TaskCompletion.SetResult(null);
        }

        public DateTime GetTime()
        {
            return _now;
        }

        public Task Delay(int milliseconds)
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
                return task.TaskCompletion.Task;
            }
        }
    }
}
