using System;
using System.Collections.Generic;
using System.Threading;

namespace ServiceLib
{
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

    public class PureProjectionProcess<TState>
        : IHandle<SystemEvents.SystemInit>
        , IHandle<SystemEvents.SystemShutdown>
    {
        private readonly string _lockName;
        private readonly IPureProjectionVersionControl _versioning;
        private readonly INodeLockManager _locking;
        private readonly IPureProjectionStateCache<TState> _store;
        private readonly IPureProjectionDispatcher<TState> _dispatcher;
        private readonly IEventStreamingDeserialized _streaming;

        private CancellationTokenSource _cancel;
        private EventStoreToken _currentToken;
        private object _currentEvent;
        private IPureProjectionHandler<TState, object> _currentHandler;
        private string _currentPartition;

        public PureProjectionProcess(
            string lockName, 
            IPureProjectionVersionControl versioning, 
            INodeLockManager locking,
            IPureProjectionStateCache<TState> store, 
            IPureProjectionDispatcher<TState> dispatcher, 
            IEventStreamingDeserialized streaming)
        {
            _lockName = lockName;
            _versioning = versioning;
            _locking = locking;
            _store = store;
            _dispatcher = dispatcher;
            _streaming = streaming;
        }

        public void Subscribe(IBus bus)
        {
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            _cancel = new CancellationTokenSource();
            _locking.Lock(_lockName, OnLockObtained, EmptyAction, false);
        }

        public void Handle(SystemEvents.SystemShutdown message)
        {
            _cancel.Cancel();
        }

        private void OnLockObtained()
        {
            _store.LoadMetadata(OnMetadataLoaded, OnError);
        }
        private void OnMetadataLoaded(string version, EventStoreToken token)
        {
            if (_cancel.IsCancellationRequested)
                StopWorking();
            else if (string.IsNullOrEmpty(version) || _versioning.NeedsRebuild(version))
            {
                _streaming.Setup(EventStoreToken.Initial, _dispatcher.GetRegisteredTypes(), _versioning.GetStreamPrefixes(), false);
                _store.Reset(_versioning.GetVersion(), ProcessEvents, OnError);
            }
            else
            {
                _streaming.Setup(token, _dispatcher.GetRegisteredTypes(), _versioning.GetStreamPrefixes(), false);
                _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, _cancel.Token, false);
            }
        }
        private void ProcessEvents()
        {
            if (_cancel.IsCancellationRequested)
                StopWorking();
            else
                _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, _cancel.Token, true);
        }
        private void OnEventReceived(EventStoreToken token, object evnt)
        {
            if (_cancel.IsCancellationRequested)
                StopWorking();
            else
            {
                _currentToken = token;
                _currentEvent = evnt;
                _currentHandler = _dispatcher.FindHandler(evnt.GetType());
                _currentPartition = _currentHandler.Partition(evnt);
                _store.Get(_currentPartition, PartitionLoaded, OnError);
            }
        }
        private void OnEventUnavailable()
        {
            _store.Flush(OnStoreFlushed, OnError);
        }
        private void OnStoreFlushed()
        {
            if (_cancel.IsCancellationRequested)
                StopWorking();
            else
                _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, _cancel.Token, false);
        }
        private void PartitionLoaded(TState state)
        {
            try
            {
                var newState = _currentHandler.ApplyEvent(state, _currentEvent, _currentToken);
                _store.Set(_currentPartition, newState, ProcessEvents, OnError);
            }
            catch (Exception exception)
            {
                OnError(exception);
            }
        }

        private void OnEventError(Exception exception, EventStoreEvent evnt)
        {
            OnError(exception);
        }
        private void OnError(Exception exception)
        {
            _cancel.Cancel();
            StopWorking();
        }
        private void StopWorking()
        {
            _locking.Unlock(_lockName);
        }
        private void EmptyAction()
        {
        }
    }
}
