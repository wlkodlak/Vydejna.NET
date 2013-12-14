using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public interface IHandle<T>
    {
        Task Handle(T message);
    }

    public interface IAnswer<TQuestion, TAnswer>
    {
        Task<TAnswer> Handle(TQuestion request);
    }

    public interface IHandleSync<T>
    {
        void Handle(T message);
    }

    public interface IHandleRegistration<T> : IDisposable
    {
        void ReplaceWith(IHandle<T> handler);
    }

    public static class TaskResult
    {
        private static readonly Task CachedCompletedTask = Task.FromResult<object>(null);

        public static Task<T> GetCompletedTask<T>(T result)
        {
            return Task.FromResult<T>(result);
        }

        public static Task GetCompletedTask()
        {
            return CachedCompletedTask;
        }

        public static Task<T> GetFailedTask<T>(Exception exception)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetException(exception);
            return tcs.Task;
        }

        public static Task GetCancelledTask()
        {
            var tcs = new TaskCompletionSource<object>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        public static Task GetFailedTask(Exception ex)
        {
            return GetFailedTask<object>(ex);
        }
    }
}
