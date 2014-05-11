using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IHandle<T>
    {
        void Handle(T message);
    }
    public interface IProcess<TCommand>
    {
        Task Handle(TCommand command);
    }
    public interface IAnswer<TQuery, TAnswer>
    {
        Task<TAnswer> Handle(TQuery query);
    }

    public interface ISubscription : IHandle<object>
    {
    }
    public interface ICommandSubscription : IProcess<object>
    {
    }
}
