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
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(result);
            return tcs.Task;
        }

        public static Task GetCompletedTask()
        {
            return GetCompletedTask<object>(null);
        }
    }
}
