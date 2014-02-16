using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventProjector
    {
        IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler);
    }
    public interface IEventProjection
        : IHandle<CommandExecution<ProjectorMessages.Reset>>
        , IHandle<CommandExecution<ProjectorMessages.UpgradeFrom>>
    {
        string GetVersion();
        EventProjectionUpgradeMode UpgradeMode(string storedVersion);
    }
    public enum EventProjectionUpgradeMode { NotNeeded, Rebuild, Upgrade }
    public static class ProjectorMessages
    {
        public class ConcurrencyException : Exception { }
        public class RebuildFinished { }
        public class UpgradeFrom
        {
            private readonly string _version;
            public string Version { get { return _version; } }
            public UpgradeFrom(string version) { _version = version; }
        }
        public class Reset { }
        public class Flush { }
        public class Resume { }
    }

    public class EventProjectorSimple : IEventProjector, IProcessWorker
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private readonly IEventProjection _projectionInfo;
        private EventStoreToken _lastCompletedToken;
        private string _version;
        private EventProjectionUpgradeMode _upgradeMode;
        private int _flushCounter;
        private bool _metadataDirty;
        private ICommandSubscription _currentHandler;
        private object _currentEvent;
        private IDisposable _waitForLock;
        private string _storedVersion;
        private EventStoreToken _currentToken;
        private bool _flushNeeded;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;

        public EventProjectorSimple(IEventProjection projection, IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _projectionInfo = projection;
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
        }

        public IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler)
        {
            return _subscriptions.Register(handler);
        }


        private void OnProjectionLocked()
        {
            _metadata.GetVersion(OnVersionLoaded, OnError);
        }
        private void OnVersionLoaded(string version)
        {
            _storedVersion = version;
            _metadata.GetToken(OnTokenLoaded, OnError);
        }
        private void OnTokenLoaded(EventStoreToken token)
        {
            try
            {
                _lastCompletedToken = token;
                _version = _projectionInfo.GetVersion();
                _upgradeMode = _projectionInfo.UpgradeMode(_storedVersion);
                if (_upgradeMode == EventProjectionUpgradeMode.Rebuild)
                    _lastCompletedToken = EventStoreToken.Initial;
                _streaming.Setup(_lastCompletedToken, _subscriptions.GetHandledTypes().ToArray(), _metadata.ProcessName);
                _flushCounter = 20;
                SetProcessState(ProcessState.Running);
                if (_upgradeMode == EventProjectionUpgradeMode.Rebuild)
                {
                    _flushNeeded = true;
                    CallHandler(new ProjectorMessages.Reset(), SaveNewVersion, OnError);
                }
                else if (_upgradeMode == EventProjectionUpgradeMode.Upgrade)
                    CallHandler(new ProjectorMessages.UpgradeFrom(_storedVersion), SaveNewVersion, OnError);
                else
                    WaitForEvents();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }
        private void CallHandler(ProjectorMessages.Reset command, Action onComplete, Action<Exception> onError)
        {
            var handler = _subscriptions.FindHandler(typeof(ProjectorMessages.Reset));
            if (handler != null)
                handler.Handle(command, onComplete, onError);
            else
                _projectionInfo.Handle(new CommandExecution<ProjectorMessages.Reset>(command, onComplete, onError));
        }
        private void CallHandler(ProjectorMessages.UpgradeFrom command, Action onComplete, Action<Exception> onError)
        {
            var handler = _subscriptions.FindHandler(typeof(ProjectorMessages.UpgradeFrom));
            if (handler != null)
                handler.Handle(command, onComplete, onError);
            else
                _projectionInfo.Handle(new CommandExecution<ProjectorMessages.UpgradeFrom>(command, onComplete, onError));
        }
        private void CallHandler<T>(T command, Action onComplete, Action<Exception> onError)
        {
            var handler = _subscriptions.FindHandler(typeof(T));
            if (handler != null)
                handler.Handle(command, onComplete, onError);
            else
                onComplete();
        }
        private void SaveNewVersion()
        {
            if (_upgradeMode == EventProjectionUpgradeMode.Rebuild)
                _metadata.SetVersion(_version, ProcessNextEvent, OnError);
            else
                _metadata.SetVersion(_version, WaitForEvents, OnError);
        }
        private void WaitForEvents()
        {
            if (_processState == ProcessState.Running)
                _streaming.GetNextEvent(OnEventReceived, OnEventsUsedUp, OnEventsError, false);
            else if (_processState == ProcessState.Pausing)
                SaveToken();
            else
                StopRunning(ProcessState.Inactive);
        }
        private void ProcessNextEvent()
        {
            if (_processState == ProcessState.Running)
                _streaming.GetNextEvent(OnEventReceived, OnEventsUsedUp, OnEventsError, true);
            else if (_processState == ProcessState.Pausing)
                SaveToken();
            else
                StopRunning(ProcessState.Inactive);
        }
        private void OnEventReceived(EventStoreToken token, object evnt)
        {
            try
            {
                _currentHandler = _subscriptions.FindHandler(evnt.GetType());
                _currentEvent = evnt;
                _currentToken = token;
                _flushNeeded = true;
                if (_currentHandler != null)
                    _currentHandler.Handle(_currentEvent, OnEventProcessed, OnError);
                else
                    OnEventProcessed();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }
        private void OnEventsUsedUp()
        {
            _currentEvent = null;
            _currentHandler = null;
            SaveToken();
        }
        private void OnEventsError(Exception exception, EventStoreEvent evnt)
        {
            OnError(exception);
        }
        private void OnEventProcessed()
        {
            _flushCounter--;
            _metadataDirty = true;
            _lastCompletedToken = _currentToken;
            if (_flushCounter <= 0)
                SaveToken();
            else
                ProcessNextEvent();
        }
        private void SaveToken()
        {
            if (_metadataDirty)
                _metadata.SetToken(_lastCompletedToken, TokenSaved, OnError);
            else if (_flushNeeded)
                TokenSaved();
            else
                FlushReported();
        }
        private void TokenSaved()
        {
            _flushNeeded = false;
            _metadataDirty = false;
            if (_upgradeMode != EventProjectionUpgradeMode.Rebuild)
            {
                _upgradeMode = EventProjectionUpgradeMode.NotNeeded;
                CallHandler(new ProjectorMessages.Flush(), FlushReported, OnNotifyError);
            }
            else if (_currentEvent != null)
                FlushReported();
            else
            {
                _upgradeMode = EventProjectionUpgradeMode.NotNeeded;
                CallHandler(new ProjectorMessages.RebuildFinished(), FlushReported, OnNotifyError);
            }
        }
        private void FlushReported()
        {
            if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                StopRunning(ProcessState.Inactive);
            else if (_currentEvent != null)
                ProcessNextEvent();
            else
                WaitForEvents();
        }

        private void OnError(Exception exception)
        {
            StopRunning(ProcessState.Faulted);
        }

        private void OnNotifyError(Exception exception)
        {
            FlushReported();
        }

        private void StopRunning(ProcessState newState)
        {
            _streaming.Dispose();
            _metadata.Unlock();
            if (_processState != ProcessState.Faulted)
                SetProcessState(newState);
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
            _waitForLock = _metadata.Lock(OnProjectionLocked);
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
            _streaming.Dispose();
            _waitForLock.Dispose();
        }

        public void Dispose()
        {
            _onStateChanged = null;
            _processState = ProcessState.Uninitialized;
        }
    }
}
