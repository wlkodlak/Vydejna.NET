using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IPureProjectionStateCache<TState>
    {
        void Get(string partition, Action<TState> onCompleted, Action<Exception> onError);
        void Set(string partition, TState state, Action onCompleted, Action<Exception> onError);
        void Flush(Action onCompleted, Action<Exception> onError);
    }
    public interface IPureProjectionSerializer<TState>
    {
        string Serialize(TState state);
        TState Deserialize(string serializedState);
        TState InitialState();
    }
    public interface IPureProjectionVersionControl
    {
        string GetVersion();
        bool NeedsRebuild(string storedVersion);
        IList<string> GetStreamPrefixes();
    }
    public interface IPureProjection<TState> : IPureProjectionVersionControl, IPureProjectionSerializer<TState>, IPureProjectionStateToken<TState>
    {
        void Subscribe(IPureProjectionDispatcher<TState> dispatcher);
    }
}
