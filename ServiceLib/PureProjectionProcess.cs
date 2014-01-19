using System;

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
    }
    public interface IPureProjection<TState> : IPureProjectionVersionControl, IPureProjectionSerializer<TState>, IPureProjectionStateToken<TState>
    {
        void Subscribe(IPureProjectionDispatcher<TState> dispatcher);
    }

    public class PureProjectionProcess<TState>
        : IProcessWorker
    {
        private readonly IPureProjectionVersionControl _versioning;
        private readonly INodeLock _locking;
        private readonly IPureProjectionStateCache<TState> _store;
        private readonly IPureProjectionDispatcher<TState> _dispatcher;
        private readonly IEventStreamingDeserialized _streaming;

        private EventStoreToken _currentToken;
        private object _currentEvent;
        private IPureProjectionHandler<TState, object> _currentHandler;
        private string _currentPartition;
        private string _processName;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;

        public PureProjectionProcess(
            string processName,
            IPureProjectionVersionControl versioning,
            INodeLock locking,
            IPureProjectionStateCache<TState> store,
            IPureProjectionDispatcher<TState> dispatcher,
            IEventStreamingDeserialized streaming)
        {
            _processName = processName;
            _versioning = versioning;
            _locking = locking;
            _store = store;
            _dispatcher = dispatcher;
            _streaming = streaming;
        }

        private void OnLockObtained()
        {
            _store.LoadMetadata(OnMetadataLoaded, OnError);
        }
        private void OnMetadataLoaded(string version, EventStoreToken token)
        {
            if (_processState == ProcessState.Starting)
            {
                SetProcessState(ProcessState.Running);
                if (string.IsNullOrEmpty(version) || _versioning.NeedsRebuild(version))
                {
                    _streaming.Setup(EventStoreToken.Initial, _dispatcher.GetRegisteredTypes(), _processName);
                    _store.Reset(_versioning.GetVersion(), ProcessEvents, OnError);
                }
                else
                {
                    _streaming.Setup(token, _dispatcher.GetRegisteredTypes(), _processName);
                    _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, false);
                }
            }
            else
                StopWorking(ProcessState.Inactive);
        }
        private void ProcessEvents()
        {
            if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                StopWorking(ProcessState.Inactive);
            else if (_processState == ProcessState.Running)
                _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, true);
        }
        private void OnEventReceived(EventStoreToken token, object evnt)
        {
            if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                StopWorking(ProcessState.Inactive);
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
            if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                StopWorking(ProcessState.Inactive);
            else if (_processState == ProcessState.Running)
                _streaming.GetNextEvent(OnEventReceived, OnEventUnavailable, OnEventError, false);
        }
        private void PartitionLoaded(TState state)
        {
            try
            {
                var newState = _currentHandler.ApplyEvent(state, _currentEvent, _currentToken);
                _store.Set(_currentPartition, newState, SaveToken, OnError);
            }
            catch (Exception exception)
            {
                OnError(exception);
            }
        }

        private void SaveToken()
        {
            _store.SetToken(_currentToken);
            ProcessEvents();
        }

        private void OnEventError(Exception exception, EventStoreEvent evnt)
        {
            OnError(exception);
        }
        private void OnError(Exception exception)
        {
            StopWorking(ProcessState.Faulted);
        }
        private void StopWorking(ProcessState newState)
        {
            _streaming.Dispose();
            _locking.Unlock();
            if (_processState != ProcessState.Faulted)
                SetProcessState(newState);
        }
        private void EmptyAction()
        {
        }

        public ProcessState State
        {
            get { return _processState; }
        }

        public void Init(Action<ProcessState> onStateChanged)
        {
            _onStateChanged = onStateChanged;
        }

        private void SetProcessState(ProcessState state)
        {
            _processState = state;
            if (_onStateChanged != null)
                _onStateChanged(state);
        }

        public void Start()
        {
            SetProcessState(ProcessState.Starting);
            _locking.Lock(OnLockObtained, EmptyAction);
        }

        public void Pause()
        {
            Stop(false);
        }

        public void Stop()
        {
            Stop(true);
        }

        private void Stop(bool immediatelly)
        {
            if (_processState != ProcessState.Running)
                SetProcessState(ProcessState.Inactive);
            else if (immediatelly)
                SetProcessState(ProcessState.Stopping);
            else
                SetProcessState(ProcessState.Pausing);
            _locking.Dispose();
            _streaming.Dispose();
        }

        public void Dispose()
        {
            _onStateChanged = null;
            _processState = ProcessState.Uninitialized;
        }
    }
}
