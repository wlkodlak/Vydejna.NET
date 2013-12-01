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
        ContinueInitial,
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

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var oth = obj as ProjectionInstanceMetadata;
            return 
                oth != null && Name == oth.Name && 
                Version == oth.Version && MinimalReader == oth.MinimalReader && 
                CurrentNode == oth.CurrentNode && Status == oth.Status;
        }

        public override string ToString()
        {
            return string.Format(
                "Instance {0} version {1} (reader {2}): {3}",
                Name, Version, MinimalReader, Status);
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
}
