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
        IList<string> GetStreamPrefixes();
        string GetVersion();
        EventProjectionUpgradeMode UpgradeMode(string storedVersion);
    }
    public enum EventProjectionUpgradeMode { NotNeeded, Rebuild, Upgrade }
    public static class ProjectorMessages
    {
        public class RebuildFinished { }
        public class UpgradeFrom
        {
            private readonly string _version;
            public string Version { get { return _version; } }
            public UpgradeFrom(string version) { _version = version; }
        }
        public class Reset { }
        public class Flush { }
    }

    public class EventProjectorSimple : IEventProjector, IHandle<SystemEvents.SystemInit>, IHandle<SystemEvents.SystemShutdown>
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private readonly IEventProjection _projectionInfo;
        private EventStoreToken _lastCompletedToken;
        private string _version;
        private EventProjectionUpgradeMode _upgradeMode;
        private CancellationTokenSource _cancel;
        private int _flushCounter;
        private bool _metadataDirty;
        private ICommandSubscription _currentHandler;
        private object _currentEvent;
        private IDisposable _waitForLock;
        private string _storedVersion;
        private EventStoreToken _currentToken;

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

        public void Subscribe(IBus bus)
        {
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            _cancel = new CancellationTokenSource();
            _waitForLock = _metadata.Lock(OnProjectionLocked);
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
                _streaming.Setup(_lastCompletedToken, _subscriptions.GetHandledTypes().ToArray(), _projectionInfo.GetStreamPrefixes(), false);
                _flushCounter = 20;
                if (_upgradeMode == EventProjectionUpgradeMode.Rebuild)
                    CallHandler(new ProjectorMessages.Reset(), SaveNewVersion, OnError);
                else if (_upgradeMode == EventProjectionUpgradeMode.Upgrade)
                    CallHandler(new ProjectorMessages.UpgradeFrom(_storedVersion), SaveNewVersion, OnError);
                else
                    ProcessNextEvent();
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
            _metadata.SetVersion(_version, ProcessNextEvent, OnError);
        }
        private void ProcessNextEvent()
        {
            if (!_cancel.IsCancellationRequested)
                _streaming.GetNextEvent(OnEventReceived, OnEventsUsedUp, OnEventsError, _cancel.Token, true);
            else
                SaveToken();
        }
        private void OnEventReceived(EventStoreToken token, object evnt)
        {
            try
            {
                _currentHandler = _subscriptions.FindHandler(evnt.GetType());
                _currentEvent = evnt;
                _currentToken = token;
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
            else
                ProcessNextEvent();
        }
        private void TokenSaved()
        {
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
            if (_cancel.IsCancellationRequested)
                StopRunning();
            else if (_currentEvent != null)
                _streaming.GetNextEvent(OnEventReceived, OnEventsUsedUp, OnEventsError, _cancel.Token, true);
            else
                _streaming.GetNextEvent(OnEventReceived, OnEventsUsedUp, OnEventsError, _cancel.Token, false);
        }

        private void OnError(Exception exception)
        {
            _cancel.Cancel();
            StopRunning();
        }

        private void OnNotifyError(Exception exception)
        {
            FlushReported();
        }

        private void StopRunning()
        {
            _metadata.Unlock();
        }

        public void Handle(SystemEvents.SystemShutdown message)
        {
            _cancel.Cancel();
            _waitForLock.Dispose();
        }
    }
}
