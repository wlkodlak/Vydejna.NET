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

    public class CommandExecution<TCommand>
    {
        private TCommand _command;
        private Action _onCompleted;
        private Action<Exception> _onError;

        public TCommand Command { get { return _command; } }
        public Action OnCompleted { get { return _onCompleted; } }
        public Action<Exception> OnError { get { return _onError; } }

        public CommandExecution(TCommand command, Action onCompleted, Action<Exception> onError)
        {
            _command = command;
            _onCompleted = onCompleted;
            _onError = onError;
        }
    }

    public class QueryExecution<TRequest, TResponse>
    {
        private TRequest _request;
        private Action<TResponse> _onCompleted;
        private Action<Exception> _onError;

        public TRequest Request { get { return _request; } }
        public Action<TResponse> OnCompleted { get { return _onCompleted; } }
        public Action<Exception> OnError { get { return _onError; } }

        public QueryExecution(TRequest request, Action<TResponse> onCompleted, Action<Exception> onError)
        {
            _request = request;
            _onCompleted = onCompleted;
            _onError = onError;
        }
    }

    public interface IAnswer<TQuestion, TAnswer> : IHandle<QueryExecution<TQuestion, TAnswer>>
    {
    }

    public interface IHandleRegistration<T> : IDisposable
    {
        void ReplaceWith(IHandle<T> handler);
    }

    public interface ISubscription : IHandle<object>
    {
    }

    public interface ISubscription<T> : IHandleRegistration<T>, ISubscription
    {
    }

    public interface ICommandSubscription
    {
        void Handle(object command, Action onComplete, Action<Exception> onError);
    }
    public interface ICommandSubscription<T> : IHandleRegistration<CommandExecution<T>>, ICommandSubscription
    {
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
