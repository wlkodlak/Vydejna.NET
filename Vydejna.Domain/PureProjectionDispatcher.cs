using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface IPureProjectionHandler<TState, TEvent>
    {
        string Partition(TEvent evnt);
        TState ApplyEvent(TState state, TEvent evnt, EventStoreToken token);
    }
    
    public interface IPureProjectionDispatcher<TState>
    {
        void Register<TEvent>(IPureProjectionHandler<TState, TEvent> handler);
        IPureProjectionHandler<TState, object> FindHandler(Type type);
        IList<Type> GetRegisteredTypes();
    }

    public interface IPureProjectionStateToken<TState>
    {
        TState SetTokenInState(TState state, EventStoreToken token);
        EventStoreToken GetTokenFromState(TState state);
    }
    
    public class PureProjectionDispatcher<TState> : IPureProjectionDispatcher<TState>
    {
        private class PureEventHandler<TEvent> : IPureProjectionHandler<TState, object>
        {
            private IPureProjectionHandler<TState, TEvent> _handler;

            public PureEventHandler(IPureProjectionHandler<TState, TEvent> handler)
            {
                _handler = handler;
            }

            public string Partition(object evnt)
            {
                return _handler.Partition((TEvent)evnt);
            }

            public TState ApplyEvent(TState state, object evnt, EventStoreToken token)
            {
                return _handler.ApplyEvent(state, (TEvent)evnt, token);
            }
        }

        private Dictionary<Type, IPureProjectionHandler<TState, object>> _handlers;

        public PureProjectionDispatcher()
        {
            _handlers = new Dictionary<Type, IPureProjectionHandler<TState, object>>();
        }

        public void Register<TEvent>(IPureProjectionHandler<TState, TEvent> handler)
        {
            _handlers[typeof(TEvent)] = new PureEventHandler<TEvent>(handler);
        }

        public IPureProjectionHandler<TState, object> FindHandler(Type type)
        {
            IPureProjectionHandler<TState, object> handler;
            _handlers.TryGetValue(type, out handler);
            return handler;
        }

        public IList<Type> GetRegisteredTypes()
        {
            return _handlers.Keys.ToList();
        }
    }

    public class PureProjectionDispatcherDeduplication<TState> : IPureProjectionDispatcher<TState>
    {
        private IPureProjectionDispatcher<TState> _dispatcher;
        private IPureProjectionStateToken<TState> _tokenAccessor;

        private class DeduplicatingHandler<TEvent> : IPureProjectionHandler<TState, TEvent>
        {
            private IPureProjectionHandler<TState, TEvent> _handler;
            private IPureProjectionStateToken<TState> _tokenAccessor;

            public DeduplicatingHandler(IPureProjectionHandler<TState, TEvent> handler, IPureProjectionStateToken<TState> tokenAccessor)
            {
                _handler = handler;
                _tokenAccessor = tokenAccessor;
            }

            public string Partition(TEvent evnt)
            {
                return _handler.Partition(evnt);
            }

            public TState ApplyEvent(TState state, TEvent evnt, EventStoreToken token)
            {
                var lastToken = _tokenAccessor.GetTokenFromState(state);
                if (EventStoreToken.Compare(lastToken, token) >= 0)
                    return state;
                var newState = _handler.ApplyEvent(state, evnt, token);
                var stateWithToken = _tokenAccessor.SetTokenInState(newState, token);
                return stateWithToken;
            }
        }
        
        public PureProjectionDispatcherDeduplication(IPureProjectionDispatcher<TState> dispatcher, IPureProjectionStateToken<TState> tokenAccessor)
        {
            _dispatcher = dispatcher;
            _tokenAccessor = tokenAccessor;
        }

        public void Register<TEvent>(IPureProjectionHandler<TState, TEvent> handler)
        {
            _dispatcher.Register(new DeduplicatingHandler<TEvent>(handler, _tokenAccessor));
        }

        public IPureProjectionHandler<TState, object> FindHandler(Type type)
        {
            return _dispatcher.FindHandler(type);
        }

        public IList<Type> GetRegisteredTypes()
        {
            return _dispatcher.GetRegisteredTypes();
        }
    }
}
