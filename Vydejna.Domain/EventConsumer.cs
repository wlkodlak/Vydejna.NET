using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public enum ProjectionRebuildType
    {
        NoRebuild,
        Initial,
        Upgrade,
        NewRebuild,
        ContinueRebuild
    }

    public enum ProjectionReadability
    {
        Unknown,
        Functional,
        Obsolete,
        RequiresUpgrade
    }

    public interface IProjectionReader
    {
        string GetVersion();
        string GetProjectionName();
        ProjectionReadability GetReadability(string minimalReaderVersion, string storedVersion);
        void UseInstance(string instanceName);
    }

    public interface IProjection : IEventConsumer
    {
        ProjectionRebuildType NeedsRebuild(string storedVersion);
        string GetVersion();
        string GetMinimalReader();
        int EventsBulkSize();
        string GenerateInstanceName(string masterName);
        Task SetInstanceName(string instanceName);
        Task StartRebuild(bool continuation);
        Task PartialCommit();
        Task CommitRebuild();
        Task StopRebuild();
        bool SupportsProcessServices();
        void SetProcessServices(IProjectionProcess process);
    }

    public interface IProjectionProcess
    {
        Task CommitProjectionProgress();
    }

    public interface IEventConsumer
    {
        string GetConsumerName();
        Task HandleShutdown();
    }

    public interface IEventStreaming
    {
        IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode);
    }

    public interface IEventStreamingInstance
    {
        Task<EventStoreEvent> GetNextEvent(CancellationToken token);
    }

    public interface IEventsConsumerMetadata
    {
        EventStoreToken GetToken();
        Task SetToken(EventStoreToken token);
    }

    public interface IProjectionMetadata
    {
        Task BuildNewInstance(string instanceName, string nodeName, string version, string minimalReader);
        Task Upgrade(string instanceName, string newVersion);
        Task UpdateStatus(string instanceName, ProjectionStatus status);
        Task<EventStoreToken> GetToken(string instanceName);
        Task SetToken(string instanceName, EventStoreToken token);
        Task<IEnumerable<ProjectionInstanceMetadata>> GetAllMetadata();
        Task<ProjectionInstanceMetadata> GetInstanceMetadata(string instanceName);
        IDisposable RegisterForChanges(string instanceName, IHandle<ProjectionMetadataChanged> handler);
    }

    public class ProjectionMetadataChanged
    {
        public string InstanceName { get; private set; }
        public ProjectionStatus Status { get; private set; }
        public ProjectionInstanceMetadata Metadata { get; private set; }
        public object LocalObject { get; private set; }

        public ProjectionMetadataChanged(ProjectionInstanceMetadata metadata, object localObject)
        {
            this.InstanceName = metadata.Name;
            this.Status = metadata.Status;
            this.Metadata = metadata;
            this.LocalObject = localObject;
        }
    }

    public static class ProjectionUtils
    {
        public static ProjectionReadability CheckReaderVersion(string minimalReaderVersion, string readerVersion, string storedVersion, string minimalStoredVersion)
        {
            if (string.CompareOrdinal(readerVersion, minimalReaderVersion) < 0)
                return ProjectionReadability.Obsolete;
            else if (string.CompareOrdinal(storedVersion, minimalStoredVersion) < 0)
                return ProjectionReadability.RequiresUpgrade;
            else
                return ProjectionReadability.Functional;
        }

        public static ProjectionRebuildType CheckWriterVersion(string storedVersion, string writerVersion)
        {
            if (string.IsNullOrEmpty(storedVersion))
                return ProjectionRebuildType.Initial;
            else if (string.CompareOrdinal(storedVersion, writerVersion) < 0)
                return ProjectionRebuildType.NewRebuild;
            else if (string.Equals(storedVersion, writerVersion, StringComparison.Ordinal))
                return ProjectionRebuildType.NoRebuild;
            else
                return ProjectionRebuildType.NoRebuild;
        }

    }

    public class ProjectionInstanceMetadata
    {
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string MinimalReader { get; private set; }
        public string CurrentNode { get; private set; }
        public ProjectionStatus Status { get; private set; }

        public ProjectionInstanceMetadata(string name, string version, string minimalReader, string currentNode, ProjectionStatus status)
        {
            this.Name = name;
            this.Version = version;
            this.MinimalReader = minimalReader;
            this.CurrentNode = currentNode;
            this.Status = status;
        }
    }

    public enum ProjectionStatus
    {
        NewBuild,
        CancelledBuild,
        Running,
        Legacy,
        Discontinued,
        Inactive
    }

    public interface IProjectionMetadataManager
    {
        Task<IProjectionMetadata> GetProjection(string projectionName);
        Task<IEventsConsumerMetadata> GetHandler(string handlerName);
    }

    public interface IEventConsumerManager : IProjectionMetadataManager, IEventStreaming
    {
        object Deserialize(EventStoreEvent evt);
    }

    public class EventsConsumerManager : IEventConsumerManager
    {
        private IProjectionMetadataManager _metadata;
        private IEventStreaming _events;
        private IEventSourcedSerializer _serializer;

        public EventsConsumerManager(IProjectionMetadataManager metadata, IEventStreaming events, IEventSourcedSerializer serializer)
        {
            _metadata = metadata;
            _events = events;
            _serializer = serializer;
        }

        public object Deserialize(EventStoreEvent evt)
        {
            return _serializer.Deserialize(evt);
        }

        public IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode)
        {
            return _events.GetStreamer(filter, token, rebuildMode);
        }

        public Task<IProjectionMetadata> GetProjection(string projectionName)
        {
            return _metadata.GetProjection(projectionName);
        }

        public Task<IEventsConsumerMetadata> GetHandler(string handlerName)
        {
            return _metadata.GetHandler(handlerName);
        }
    }

    public abstract class ProjectionProxy<T> : IHandle<ProjectionMetadataChanged>
        where T : class, IProjectionReader
    {
        private IProjectionMetadataManager _mgr;
        private List<ReaderInfo> _readers;
        private T _currentReader;
        private string _currentInstance;
        private string _projectionName;
        private IProjectionMetadata _metadata;
        private List<ProjectionInstanceMetadata> _instances;
        private IDisposable _changeRegistration;
        private object _lock;
        private bool _isInitialized;
        private ComparerForMetadata _comparer;
        private bool _anotherPickNeeded;
        private bool _pickInProgress;

        private class ReaderInfo
        {
            public T Reader;
            public ProjectionInstanceMetadata BestMetadata;
            public ProjectionReadability Readability;
            
            public ReaderInfo(T reader)
            {
                this.Reader = reader;
            }
        }

        protected T Reader
        {
            get { return _currentReader; }
        }

        protected ProjectionProxy(IProjectionMetadataManager metadataMgr, string projectionName)
        {
            _mgr = metadataMgr;
            _projectionName = projectionName;
            _readers = new List<ReaderInfo>();
            _lock = new object();
            _comparer = new ComparerForMetadata();
        }

        public void Register(T reader)
        {
            lock (_lock)
            {
                _readers.Add(new ReaderInfo(reader));
                _anotherPickNeeded = _isInitialized;
            }
            PickInstance();
        }

        public async Task InitializeInstances()
        {
            _metadata = await _mgr.GetProjection(_projectionName);
            _changeRegistration = _metadata.RegisterForChanges(null, this);
            var allInstances = await _metadata.GetAllMetadata();
            lock (_lock)
            {
                _instances = PreprocessInstances(allInstances);
                _isInitialized = true;
                _anotherPickNeeded = true;
            }
            PickInstance();
        }

        private List<ProjectionInstanceMetadata> PreprocessInstances(IEnumerable<ProjectionInstanceMetadata> allInstances)
        {
            return allInstances.Where(i => IsStatusAlive(i.Status)).OrderByDescending(i => i.Version).ThenBy(i => i.Name).ToList();
        }

        private bool IsStatusAlive(ProjectionStatus projectionStatus)
        {
            switch (projectionStatus)
            {
                case ProjectionStatus.NewBuild:
                case ProjectionStatus.Running:
                case ProjectionStatus.Legacy:
                    return true;
                default:
                    return false;
            }
        }

        public void Handle(ProjectionMetadataChanged message)
        {
            lock (_lock)
            {
                _anotherPickNeeded = true;
                int index = _instances.BinarySearch(message.Metadata, _comparer);
                if (index < 0)
                {
                    if (IsStatusAlive(message.Status))
                        _instances.Insert(~index, message.Metadata);
                    else
                        _anotherPickNeeded = false;
                }
                else
                {
                    if (IsStatusAlive(message.Status))
                        _instances[index] = message.Metadata;
                    else
                        _instances.RemoveAt(index);
                }
                if (!_isInitialized)
                    _anotherPickNeeded = false;
            }
            PickInstance();
        }

        private class ComparerForMetadata : IComparer<ProjectionInstanceMetadata>
        {
            public int Compare(ProjectionInstanceMetadata x, ProjectionInstanceMetadata y)
            {
                if (ReferenceEquals(x, null))
                    return ReferenceEquals(y, null) ? 0 : 1;
                else if (ReferenceEquals(y, null))
                    return -1;
                else
                {
                    var byVersion = -string.CompareOrdinal(x.Version, y.Version);
                    if (byVersion != 0)
                        return byVersion;
                    return string.CompareOrdinal(x.Name, y.Name);
                }
            }
        }

        [Flags]
        private enum PickInstanceNofification
        {
            None = 0,
            ResetOriginalInstance = 1,
            ChangeNewInstance = 2,
            RaiseReaderChange = 4
        }

        private void PickInstance()
        {
            bool performPick;
            PickInstanceNofification notifications;

            lock (_lock)
            {
                if (_pickInProgress)
                    return;
                performPick = _anotherPickNeeded && _isInitialized;
                _pickInProgress = true;
            }
            while (performPick)
            {
                T originalReader;

                lock (_lock)
                {
                    _anotherPickNeeded = false;
                    originalReader = _currentReader;

                    ReaderInfo bestReader = PickBestReader();
                    notifications = ChangeToBestReader(bestReader);
                }
                SendNotifications(originalReader, notifications);
                lock (_lock)
                    performPick = _anotherPickNeeded;
            }
            lock (_lock)
                _pickInProgress = false;
        }

        private ReaderInfo PickBestReader()
        {
            ReaderInfo bestReader = null;
            foreach (var readerInfo in _readers)
            {
                var candidates = _instances
                    .Select(i => new { Instance = i, Readability = readerInfo.Reader.GetReadability(i.MinimalReader, i.Version) })
                    .ToList();
                var bestCandidate = candidates.FirstOrDefault(c => c.Readability == ProjectionReadability.Functional) ?? candidates.FirstOrDefault();
                if (bestCandidate == null)
                {
                    readerInfo.Readability = ProjectionReadability.Unknown;
                    readerInfo.BestMetadata = null;
                }
                else
                {
                    readerInfo.Readability = bestCandidate.Readability;
                    readerInfo.BestMetadata = bestCandidate.Instance;
                    if (bestReader == null)
                        bestReader = readerInfo;
                    else if (string.CompareOrdinal(readerInfo.BestMetadata.Version, bestReader.BestMetadata.Version) > 0)
                        bestReader = readerInfo;
                }
            }
            return bestReader;
        }

        private PickInstanceNofification ChangeToBestReader(ReaderInfo bestReader)
        {
            if (bestReader == null)
            {
                if (_currentReader != null)
                {
                    _currentReader = null;
                    _currentInstance = null;
                    return PickInstanceNofification.ResetOriginalInstance | PickInstanceNofification.RaiseReaderChange;
                }
            }
            else if (bestReader == _currentReader)
            {
                var newInstance = bestReader.BestMetadata.Name;
                if (!string.Equals(_currentInstance, newInstance, StringComparison.Ordinal))
                {
                    _currentInstance = newInstance;
                    return PickInstanceNofification.ChangeNewInstance;
                }
            }
            else if (bestReader != _currentReader)
            {
                _currentReader = bestReader.Reader;
                _currentInstance = bestReader.BestMetadata.Name;
                return PickInstanceNofification.ResetOriginalInstance | PickInstanceNofification.RaiseReaderChange;
            }
            return PickInstanceNofification.None;
        }

        private void SendNotifications(T originalReader, PickInstanceNofification notifications)
        {
            if ((notifications & PickInstanceNofification.ResetOriginalInstance) != PickInstanceNofification.None)
                originalReader.UseInstance(null);
            if ((notifications & PickInstanceNofification.ChangeNewInstance) != PickInstanceNofification.None)
                _currentReader.UseInstance(_currentInstance);
            if ((notifications & PickInstanceNofification.RaiseReaderChange) != PickInstanceNofification.None)
                OnReaderChanged(originalReader);
        }

        protected virtual void OnReaderChanged(T original)
        {
        }
    }
}
