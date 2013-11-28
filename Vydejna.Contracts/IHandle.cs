using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public interface IHandle<T>
    {
        void Handle(T message);
    }

    public static class TaskResult
    {
        public static Task<T> GetCompletedTask<T>(T result)
        {
            return Task.FromResult<T>(result);
        }

        public static Task GetCompletedTask()
        {
            return Task.FromResult<object>(null);
        }

        public static Task<T> GetFailedTask<T>(Exception exception)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetException(exception);
            return tcs.Task;
        }
    }
}
