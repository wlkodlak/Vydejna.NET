using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    public interface IProjection : IEventConsumer
    {
        ProjectionRebuildType NeedsRebuild(string storedVersion);
        string GetVersion();
        int EventsBulkSize();
        Task StartRebuild(bool continuation);
        Task PartialCommit();
        Task CommitRebuild();
        Task StopRebuild();
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
        Task<EventStoreEvent> GetNextEvent();
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
        EventStoreToken GetToken(string instanceName);
        Task SetToken(string instanceName, EventStoreToken token);
        Task<IEnumerable<ProjectionInstanceMetadata>> GetAllMetadata();
        Task<ProjectionInstanceMetadata> GetInstanceMetadata(string instanceName);
    }

    public class ProjectionInstanceMetadata
    {
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string MinimalReader { get; private set; }
        public string CurrentNode { get; private set; }
        
        public ProjectionInstanceMetadata(string name, string version, string minimalReader, string currentNode)
        {
            this.Name = name;
            this.Version = version;
            this.MinimalReader = minimalReader;
            this.CurrentNode = currentNode;
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

    public interface IEventHandlerMetadataManager
    {
        Task<IProjectionMetadata> GetProjection(string projectionName);
        Task<IEventsConsumerMetadata> GetHandler(string handlerName);
    }

    public interface IEventConsumerManager : IEventHandlerMetadataManager, IEventStreaming
    {
        object Deserialize(EventStoreEvent evt);
    }

    public class EventsConsumerManager : IEventConsumerManager
    {
        private IEventHandlerMetadataManager _metadata;
        private IEventStreaming _events;
        private IEventSourcedSerializer _serializer;
        
        public EventsConsumerManager(IEventHandlerMetadataManager metadata, IEventStreaming events, IEventSourcedSerializer serializer)
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
}
