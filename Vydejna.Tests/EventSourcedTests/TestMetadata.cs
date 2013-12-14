using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    public class TestProjectionMetadata : IProjectionMetadata
    {
        private Dictionary<string, ProjectionInstanceMetadata> _metadata;
        private Dictionary<string, EventStoreToken> _tokens = new Dictionary<string, EventStoreToken>();
        private List<Tuple<string, IHandle<ProjectionMetadataChanged>>> _changes = new List<Tuple<string, IHandle<ProjectionMetadataChanged>>>();

        public TestProjectionMetadata(List<ProjectionInstanceMetadata> metadata)
        {
            _metadata = metadata.ToDictionary(m => m.Name);
        }

        public Task BuildNewInstance(string instanceName, string nodeName, string version, string minimalReader)
        {
            var newMetadata = new ProjectionInstanceMetadata(instanceName, version, minimalReader, null, ProjectionStatus.NewBuild);
            _metadata[instanceName] = newMetadata;
            return RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
        }

        public Task Upgrade(string instanceName, string newVersion)
        {
            var old = _metadata[instanceName];
            var newMetadata = new ProjectionInstanceMetadata(old.Name, newVersion, old.MinimalReader, null, old.Status);
            _metadata[instanceName] = newMetadata;
            return RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
        }

        public Task UpdateStatus(string instanceName, ProjectionStatus status)
        {
            var old = _metadata[instanceName];
            var newMetadata = new ProjectionInstanceMetadata(old.Name, old.Version, old.MinimalReader, null, status);
            _metadata[instanceName] = newMetadata;
            return RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
        }

        private async Task RaiseChanges(ProjectionMetadataChanged evt)
        {
            var handlers = _changes.Where(t => t.Item1 == null || t.Item1 == evt.InstanceName).Select(i => i.Item2).ToList();
            foreach (var handler in handlers)
                await handler.Handle(evt);
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
            public TestProjectionMetadata metadata;
            public Tuple<string, IHandle<ProjectionMetadataChanged>> tuple;
            public void Dispose() { metadata._changes.Remove(tuple); }
        }

        public void SetMetadata(ProjectionInstanceMetadata metadata)
        {
            _metadata[metadata.Name] = metadata;
        }
    }

    public class TestStreamer : IEventStreaming
    {
        private IList<EventStoreEvent> _events;
        private Func<CancellationToken, Task> _waitForExit;
        public TestStreamer(IList<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit)
        {
            _events = events ?? new EventStoreEvent[0];
            _waitForExit = waitForExit ?? (c => TaskResult.GetCompletedTask());
        }
        public IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode)
        {
            var typeNames = new HashSet<string>(filter.Select(t => t.Name));
            return new TestStream(_events, _waitForExit, typeNames, token, rebuildMode);
        }
    }

    public class TestStream : IEventStreamingInstance
    {
        private IList<EventStoreEvent> _events;
        private Func<CancellationToken, Task> _waitForExit;
        private HashSet<string> _types;
        private EventStoreToken _token;
        private bool _wasTokenFound;
        private int _position;
        private EventStoreEvent _current;
        private bool _readerInRebuild;
        private bool _eventInRebuild;

        public TestStream(IList<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit, HashSet<string> types, EventStoreToken token, bool rebuildMode)
        {
            _position = 0;
            _events = events;
            _waitForExit = waitForExit;
            _types = types;
            _token = token;
            _readerInRebuild = rebuildMode;
            _wasTokenFound = token == EventStoreToken.Initial;
            _current = null;
            _eventInRebuild = true;
        }

        private bool MoveNext()
        {
            if (_position >= _events.Count)
                return false;
            _current = _events[_position];
            _position++;
            return true;
        }

        public async Task<EventStoreEvent> GetNextEvent(CancellationToken cancel)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                if (!_eventInRebuild && _readerInRebuild)
                    throw new InvalidOperationException("GetNextEvent called after it returned null");
                if (!MoveNext())
                {
                    await _waitForExit(cancel);
                    continue;
                }
                if (_current == null)
                {
                    _eventInRebuild = false;
                    return _current;
                }
                if (!_wasTokenFound)
                {
                    if (_token == _current.Token)
                        _wasTokenFound = true;
                    continue;
                }
                if (!_types.Contains(_current.Type))
                    continue;
                return _current;
            }
        }
    }

    public class TestConsumerMetadata : IEventsConsumerMetadata
    {
        private EventStoreToken _token;

        public EventStoreToken GetToken()
        {
            return _token;
        }

        public Task SetToken(EventStoreToken token)
        {
            _token = token;
            return TaskResult.GetCompletedTask();
        }
    }
}
