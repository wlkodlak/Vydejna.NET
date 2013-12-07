using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    public class TestMetadata : IProjectionMetadata
    {
        private Dictionary<string, ProjectionInstanceMetadata> _metadata;
        private Dictionary<string, EventStoreToken> _tokens = new Dictionary<string, EventStoreToken>();
        private List<Tuple<string, IHandle<ProjectionMetadataChanged>>> _changes = new List<Tuple<string, IHandle<ProjectionMetadataChanged>>>();

        public TestMetadata(List<ProjectionInstanceMetadata> metadata)
        {
            _metadata = metadata.ToDictionary(m => m.Name);
        }

        public Task BuildNewInstance(string instanceName, string nodeName, string version, string minimalReader)
        {
            var newMetadata = new ProjectionInstanceMetadata(instanceName, version, minimalReader, null, ProjectionStatus.NewBuild);
            _metadata[instanceName] = newMetadata;
            RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
            return TaskResult.GetCompletedTask();
        }

        public Task Upgrade(string instanceName, string newVersion)
        {
            var old = _metadata[instanceName];
            var newMetadata = new ProjectionInstanceMetadata(old.Name, newVersion, old.MinimalReader, null, old.Status);
            _metadata[instanceName] = newMetadata;
            RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
            return TaskResult.GetCompletedTask();
        }

        public Task UpdateStatus(string instanceName, ProjectionStatus status)
        {
            var old = _metadata[instanceName];
            var newMetadata = new ProjectionInstanceMetadata(old.Name, old.Version, old.MinimalReader, null, status);
            _metadata[instanceName] = newMetadata;
            RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
            return TaskResult.GetCompletedTask();
        }

        private void RaiseChanges(ProjectionMetadataChanged evt)
        {
            var handlers = _changes.Where(t => t.Item1 == null || t.Item1 == evt.InstanceName).Select(i => i.Item2).ToList();
            foreach (var handler in handlers)
                handler.Handle(evt);
        }

        public Task<EventStoreToken> GetToken(string instanceName)
        {
            return TaskResult.GetCompletedTask(_tokens.Where(t => t.Key == instanceName).Select(t => t.Value).FirstOrDefault());
        }

        public Task SetToken(string instanceName, EventStoreToken token)
        {
            _tokens[instanceName] = token;
            return TaskResult.GetCompletedTask();
        }

        public Task<IEnumerable<ProjectionInstanceMetadata>> GetAllMetadata()
        {
            return TaskResult.GetCompletedTask(_metadata.Values.AsEnumerable());
        }

        public Task<ProjectionInstanceMetadata> GetInstanceMetadata(string instanceName)
        {
            return TaskResult.GetCompletedTask(_metadata.Values.FirstOrDefault(m => m.Name == instanceName));
        }

        public IDisposable RegisterForChanges(string instanceName, IHandle<ProjectionMetadataChanged> handler)
        {
            var tuple = new Tuple<string, IHandle<ProjectionMetadataChanged>>(instanceName, handler);
            _changes.Add(tuple);
            return new UnregisterMetadataChanges { metadata = this, tuple = tuple };
        }

        private class UnregisterMetadataChanges : IDisposable
        {
            public TestMetadata metadata;
            public Tuple<string, IHandle<ProjectionMetadataChanged>> tuple;
            public void Dispose() { metadata._changes.Remove(tuple); }
        }

        public void SetMetadata(ProjectionInstanceMetadata metadata)
        {
            _metadata[metadata.Name] = metadata;
        }
    }

}
