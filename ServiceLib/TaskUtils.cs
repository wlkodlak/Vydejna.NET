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
            return new TaskContinuationBuilder<T>(tasks);
        }
    }

    public class TaskContinuationBuilder<T>
    {
        private IEnumerable<Task> _tasks;

        public TaskContinuationBuilder(IEnumerable<Task> tasks)
        {
            _tasks = tasks;
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
}
