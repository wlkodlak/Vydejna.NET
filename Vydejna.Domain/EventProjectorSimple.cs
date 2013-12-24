using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
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
        private readonly IEventStreaming _streaming;
        private readonly IEventSourcedSerializer _serializer;
        private readonly ICommandSubscriptionManager _subscriptions;
        private readonly IEventProjection _projectionInfo;
        private EventStoreToken _token;
        private string _version;
        private EventProjectionUpgradeMode _upgradeMode;
        private IEventStreamer _streamer;
        private CancellationTokenSource _cancel;
        private EventStoreEvent _currentEvent;
        private int _flushCounter;
        private bool _metadataDirty;
        private bool _isRunning;
        private ICommandSubscription _currentHandler;
        private object _currentObjectEvent;

        public EventProjectorSimple(IEventProjection projection, IMetadataInstance metadata, IEventStreaming streaming, IEventSourcedSerializer serializer, ICommandSubscriptionManager subscriptions)
        {
            _projectionInfo = projection;
            _metadata = metadata;
            _streaming = streaming;
            _serializer = serializer;
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
            _metadata.Lock(OnProjectionLocked);
        }

        private void OnProjectionLocked()
        {
            _isRunning = true;
            _metadata.GetData(OnMetadataLoaded, OnError);
        }
        private void OnMetadataLoaded(MetadataInfo info)
        {
            var handledTypeNames = _subscriptions.GetHandledTypes().Select(_serializer.GetTypeName).ToArray();
            _token = info.Token;
            _version = _projectionInfo.GetVersion();
            _upgradeMode = _projectionInfo.UpgradeMode(info.Version);
            _streamer = _streaming.GetStreamer(new EventStreamingFilter(_token, handledTypeNames, _projectionInfo.GetStreamPrefixes()));
            _flushCounter = 20;
            if (_upgradeMode == EventProjectionUpgradeMode.Rebuild)
                CallHandler(new ProjectorMessages.Reset(), ProcessNextEvent, OnError);
            else if (_upgradeMode == EventProjectionUpgradeMode.Upgrade)
                CallHandler(new ProjectorMessages.UpgradeFrom(info.Version), ProcessNextEvent, OnError);
            else
                ProcessNextEvent();
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
        private void ProcessNextEvent()
        {
            if (!_cancel.IsCancellationRequested)
                _streamer.GetNextEvent(OnEventReceived, _cancel.Token, true);
            else
                SaveMetadata();
        }
        private void OnEventReceived(EventStoreEvent evnt)
        {
            _currentEvent = evnt;
            if (_currentEvent != null)
            {
                _currentHandler = _subscriptions.FindHandler(_serializer.GetTypeFromName(_currentEvent.Type));
                if (_currentHandler != null)
                {
                    _currentObjectEvent = _serializer.Deserialize(evnt);
                    _currentHandler.Handle(_currentObjectEvent, OnEventProcessed, OnError);
                }
                else
                    OnEventProcessed();
            }
            else
                SaveMetadata();
        }
        private void OnEventProcessed()
        {
            _flushCounter--;
            _metadataDirty = true;
            _token = _currentEvent.Token;
            if (_flushCounter <= 0)
                SaveMetadata();
            else
                ProcessNextEvent();
        }
        private void SaveMetadata()
        {
            if (_metadataDirty)
                _metadata.SetData(new MetadataInfo(_token, _version), MetadataSaved, OnError);
            else
                ProcessNextEvent();
        }
        private void MetadataSaved()
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
                _streamer.GetNextEvent(OnEventReceived, _cancel.Token, true);
            else
                _streamer.GetNextEvent(OnEventReceived, _cancel.Token, false);
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
            _metadata.CancelLock();
            _isRunning = false;
        }

        public void Handle(SystemEvents.SystemShutdown message)
        {
            _cancel.Cancel();
            if (!_isRunning)
                _metadata.CancelLock();
        }
    }
}
