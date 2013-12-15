using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IProjectionProcessHandlerCollection
    {
        IHandleRegistration<T> Register<T>(IHandle<T> handler);
        Task Handle(Type type, object evt);
        ICollection<Type> HandledTypes();
    }

    public class ProjectionProcess : IHandle<ProjectionMetadataChanged>, IProjectionProcess, IDisposable
    {
        private string _instanceName;
        private IEventStreaming _streamer;
        private IProjectionMetadataManager _metadataManager;
        private IProjectionMetadata _metadata;
        private IProjection _projection;
        private SubscriptionManager _handlers;
        private IEventSourcedSerializer _serializer;
        private EventStoreToken _currentToken;
        private IEventStreamingInstance _openedEventStream;
        private CancellationTokenSource _cancel;
        private bool _isRebuilder;
        private bool _isReplaced;
        private IEnumerable<ProjectionInstanceMetadata> _allMetadata;
        private ProjectionInstanceMetadata _runningMetadata;
        private ProjectionInstanceMetadata _rebuildMetadata;
        private ProjectionRebuildType _rebuildType;
        private bool _isInRebuildMode;
        private bool _isInBatchMode;
        private Task _processTask;

        public ProjectionProcess(IEventStreaming streamer, IProjectionMetadataManager metadataManager, IEventSourcedSerializer serializer)
        {
            _streamer = streamer;
            _metadataManager = metadataManager;
            _serializer = serializer;
            _handlers = new SubscriptionManager();
            _cancel = new CancellationTokenSource();
        }

        public ProjectionProcess Setup(IProjection projection)
        {
            _projection = projection;
            return this;
        }

        public ProjectionProcess AsMaster()
        {
            return this;
        }

        public ProjectionProcess AsRebuilder()
        {
            _isRebuilder = true;
            return this;
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            return _handlers.Register(handler);
        }

        public void Start()
        {
            _processTask = SetupAndRun();
        }

        private async Task SetupAndRun()
        {
            await LoadMetadata().ConfigureAwait(false);
            DetectRebuildType();

            if (!_isRebuilder)
                await InitializeAsMaster().ConfigureAwait(false);
            else if (!await InitializeAsRebuilder().ConfigureAwait(false))
                return;

            await Run().ConfigureAwait(false);
        }

        private async Task LoadMetadata()
        {
            _metadata = await _metadataManager.GetProjection(_projection.GetConsumerName()).ConfigureAwait(false);
            _allMetadata = await _metadata.GetAllMetadata().ConfigureAwait(false);
            _runningMetadata = _allMetadata.FirstOrDefault(m => m.Status == ProjectionStatus.Running);
            _rebuildMetadata = _allMetadata.FirstOrDefault(m => m.Status == ProjectionStatus.NewBuild);
        }

        private void DetectRebuildType()
        {
            if (_runningMetadata == null)
            {
                if (_rebuildMetadata == null)
                {
                    _rebuildType = ProjectionRebuildType.Initial;
                    _instanceName = _isRebuilder ? null : _projection.GenerateInstanceName(null);
                }
                else
                {
                    _rebuildType = _projection.NeedsRebuild(_rebuildMetadata.Version);
                    _instanceName = _isRebuilder ? null : _rebuildMetadata.Name;
                    if (_rebuildType == ProjectionRebuildType.NoRebuild)
                        _rebuildType = ProjectionRebuildType.ContinueInitial;
                    else
                        _rebuildType = ProjectionRebuildType.Initial;
                }
            }
            else
            {
                _rebuildType = _projection.NeedsRebuild(_runningMetadata.Version);
                if (_rebuildType == ProjectionRebuildType.NoRebuild)
                    _instanceName = _isRebuilder ? null : _runningMetadata.Name;
                else if (_rebuildMetadata == null)
                    _instanceName = _isRebuilder ? _projection.GenerateInstanceName(_runningMetadata.Name) : _runningMetadata.Name;
                else
                {
                    _instanceName = _isRebuilder ? _rebuildMetadata.Name : _runningMetadata.Name;
                    _rebuildType = _projection.NeedsRebuild(_rebuildMetadata.Version);
                    if (_rebuildType == ProjectionRebuildType.NoRebuild)
                        _rebuildType = ProjectionRebuildType.ContinueRebuild;
                }
            }
        }

        private async Task InitializeAsMaster()
        {
            await _projection.SetInstanceName(_instanceName).ConfigureAwait(false);
            if (_rebuildType == ProjectionRebuildType.Initial)
                _currentToken = EventStoreToken.Initial;
            else
                _currentToken = await _metadata.GetToken(_instanceName).ConfigureAwait(false);
            if (_rebuildType == ProjectionRebuildType.Initial)
            {
                await _metadata.BuildNewInstance(_instanceName, null, _projection.GetVersion(), _projection.GetMinimalReader()).ConfigureAwait(false);
                await _projection.StartRebuild(false).ConfigureAwait(false);
            }
            else if (_rebuildType == ProjectionRebuildType.ContinueInitial)
                await _projection.StartRebuild(true).ConfigureAwait(false);
            _metadata.RegisterForChanges(_instanceName, this);
            _isInRebuildMode = _rebuildType == ProjectionRebuildType.Initial || _rebuildType == ProjectionRebuildType.ContinueInitial;
            _openedEventStream = _streamer.GetStreamer(_handlers.GetHandledTypes(), _currentToken, _isInRebuildMode);
        }

        private async Task<bool> InitializeAsRebuilder()
        {
            await _projection.SetInstanceName(_instanceName).ConfigureAwait(false);
            if (_rebuildType == ProjectionRebuildType.NewRebuild)
            {
                _currentToken = EventStoreToken.Initial;
                await _metadata
                    .BuildNewInstance(_instanceName, null, _projection.GetVersion(), _projection.GetMinimalReader())
                    .ConfigureAwait(false);
                await _projection.StartRebuild(false).ConfigureAwait(false);
            }
            else if (_rebuildType == ProjectionRebuildType.ContinueRebuild)
            {
                _currentToken = await _metadata.GetToken(_instanceName).ConfigureAwait(false);
                await _projection.StartRebuild(true).ConfigureAwait(false);
            }
            else
                return false;
            _metadata.RegisterForChanges(_instanceName, this);
            _isInRebuildMode = true;
            _openedEventStream = _streamer.GetStreamer(_handlers.GetHandledTypes(), _currentToken, true);
            return true;
        }

        private async Task Run()
        {
            var supportsBatchMode = _projection.SupportsProcessServices();
            while (!_cancel.IsCancellationRequested && !_isReplaced)
            {
                try
                {
                    var storedEvent = await _openedEventStream.GetNextEvent(_cancel.Token).ConfigureAwait(false);
                    if (storedEvent != null)
                    {
                        if (!_isInRebuildMode && !_isInBatchMode && supportsBatchMode)
                        {
                            _projection.SetProcessServices(this);
                            _isInBatchMode = true;
                        }

                        var objectEvent = _serializer.Deserialize(storedEvent);
                        var handlers = _handlers.FindHandlers(objectEvent.GetType());
                        foreach (var handler in handlers)
                            await handler.Handle(objectEvent).ConfigureAwait(false);
                        _currentToken = storedEvent.Token;

                        if (!_isInBatchMode && !_isInRebuildMode)
                            await _metadata.SetToken(_instanceName, _currentToken).ConfigureAwait(false);
                        else
                            System.Diagnostics.Debug.WriteLine("Skipping SetToken({0})", _currentToken);
                    }
                    else if (_isInRebuildMode)
                    {
                        await _projection.CommitRebuild().ConfigureAwait(false);
                        await _metadata.UpdateStatus(_instanceName, ProjectionStatus.Running).ConfigureAwait(false);
                        await _metadata.SetToken(_instanceName, _currentToken).ConfigureAwait(false);
                        if (_isRebuilder)
                            await _metadata.UpdateStatus(_runningMetadata.Name, ProjectionStatus.Legacy).ConfigureAwait(false);
                        _openedEventStream = _streamer.GetStreamer(_handlers.GetHandledTypes(), _currentToken, false);
                        _isInRebuildMode = false;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            await _projection.HandleShutdown().ConfigureAwait(false);
        }

        Task IHandle<ProjectionMetadataChanged>.Handle(ProjectionMetadataChanged msg)
        {
            if (_instanceName == msg.InstanceName && StatusStopsHandling(msg.Status))
            {
                _isReplaced = true;
                _cancel.Cancel();
            }
            return TaskResult.GetCompletedTask();
        }

        private bool StatusStopsHandling(ProjectionStatus status)
        {
            switch (status)
            {
                case ProjectionStatus.NewBuild:
                case ProjectionStatus.Running:
                    return false;
                default:
                    return true;
            }
        }

        public void Stop()
        {
            _cancel.Cancel();
            _processTask.Wait();
            _cancel.Dispose();
        }

        void IDisposable.Dispose()
        {
            Stop();
        }

        public Task CommitProjectionProgress()
        {
            return _metadata.SetToken(_instanceName, _currentToken);
        }
    }
}
