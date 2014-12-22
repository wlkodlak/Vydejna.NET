using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IHandle<T>
    {
        void Handle(T message);
    }

    public interface IProcessEvent<TEvent>
    {
        Task Handle(TEvent command);
    }

    public interface IProcessCommand<TCommand>
    {
        Task<CommandResult> Handle(TCommand command);
    }

    public interface IAnswer<TQuery, TAnswer>
    {
        Task<TAnswer> Handle(TQuery query);
    }

    public interface ISubscription : IHandle<object>
    {
    }

    public interface ICommandSubscription : IProcessCommand<object>
    {
    }

    public interface IEventSubscription : IProcessEvent<object>
    {
    }
}