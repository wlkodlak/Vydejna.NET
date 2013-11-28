﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using System.Xml.Linq;

namespace Vydejna.Domain
{
    public class ProjectionMetadataManager : IProjectionMetadataManager
    {
        private IDocumentStore _store;
        private string _prefix;
        private object _lock;
        private Dictionary<string, EventsConsumerMetadata> _consumers;
        private Dictionary<string, ProjectionMetadata> _projections;

        public ProjectionMetadataManager(IDocumentStore store, string prefix)
        {
            _store = store;
            _prefix = prefix ?? "Projections";
            _lock = new object();
            _consumers = new Dictionary<string, EventsConsumerMetadata>();
            _projections = new Dictionary<string, ProjectionMetadata>();
        }

        public async Task<IProjectionMetadata> GetProjection(string projectionName)
        {
            ProjectionMetadata metadata;
            lock (_lock)
            {
                if (!_projections.TryGetValue(projectionName, out metadata))
                    _projections[projectionName] = metadata = new ProjectionMetadata(this, projectionName);
            }
            await metadata.Load();
            return metadata;
        }

        public async Task<IEventsConsumerMetadata> GetHandler(string handlerName)
        {
            EventsConsumerMetadata metadata;
            lock (_lock)
            {
                if (!_consumers.TryGetValue(handlerName, out metadata))
                    _consumers[handlerName] = metadata = new EventsConsumerMetadata(this, handlerName);
            }
            await metadata.Load();
            return metadata;
        }

        private string BuildMetadataDocumentName(string consumerName)
        {
            return _prefix + "::" + consumerName;
        }

        private string BuildInstanceDocumentName(string consumerName, string instanceName)
        {
            return _prefix + "::" + consumerName + "::I" + instanceName;
        }

        private string BuildTokenDocumentName(string consumerName, string instanceName)
        {
            return _prefix + "::" + consumerName + "::T" + instanceName;
        }

        private class EventsConsumerMetadata : IEventsConsumerMetadata
        {
            private ProjectionMetadataManager _parent;
            private EventStoreToken _token;
            private string _consumerName;
            private string _tokenDocument;
            private AsyncLock _lock;

            public EventsConsumerMetadata(ProjectionMetadataManager parent, string consumerName)
            {
                _parent = parent;
                _consumerName = consumerName;
                _tokenDocument = parent.BuildMetadataDocumentName(consumerName);
                _lock = new AsyncLock();
            }

            public async Task Load()
            {
                using (await _lock.Lock())
                {
                    _token = new EventStoreToken(await _parent._store.GetDocument(_tokenDocument));
                }
            }

            public EventStoreToken GetToken()
            {
                return _token;
            }

            public async Task SetToken(EventStoreToken token)
            {
                using (await _lock.Lock())
                {
                    _token = token;
                    await _parent._store.SaveDocument(_tokenDocument, _token.ToString());
                }
            }
        }

        private class InstanceMetadata
        {
            public string InstanceName;
            public string RunningNode;
            public string Version;
            public string ReaderVersion;
            public ProjectionStatus Status;
            public string Token;
            public AsyncLock TokenLock;
        }

        private class ProjectionMetadata : IProjectionMetadata
        {
            private AsyncLock _lock;
            private ProjectionMetadataManager _parent;
            private string _projectionName;
            private Dictionary<string, InstanceMetadata> _instances;
            private string _documentName;
            private bool _isLoaded;
            private List<ChangesRegistration> _changes;

            public ProjectionMetadata(ProjectionMetadataManager parent, string projectionName)
            {
                _parent = parent;
                _projectionName = projectionName;
                _lock = new AsyncLock();
                _instances = new Dictionary<string, InstanceMetadata>();
                _documentName = _parent.BuildMetadataDocumentName(projectionName);
                _changes = new List<ChangesRegistration>();
            }

            public async Task Load()
            {
                using (await _lock.Lock())
                {
                    if (_isLoaded)
                        return;

                    var doc = await _parent._store.GetDocument(_documentName);
                    if (!string.IsNullOrEmpty(doc))
                    {
                        var xml = XElement.Parse(doc);
                        foreach (var xmlInstance in xml.Elements("Instance"))
                        {
                            var instance = new InstanceMetadata();
                            instance.InstanceName = (string)xmlInstance.Attribute("InstanceName");
                            instance.RunningNode = (string)xmlInstance.Attribute("RunningNode");
                            instance.Version = (string)xmlInstance.Attribute("Version");
                            instance.ReaderVersion = (string)xmlInstance.Attribute("ReaderVersion");
                            instance.Status = StatusFromString((string)xmlInstance.Attribute("Status"));
                            _instances.Add(instance.InstanceName, instance);
                        }
                    }
                    _isLoaded = true;
                }
            }

            private ProjectionStatus StatusFromString(string s)
            {
                ProjectionStatus result;
                if (Enum.TryParse<ProjectionStatus>(s, out result))
                    return result;
                else
                    return ProjectionStatus.Inactive;
            }

            private void RaiseChanges(List<ProjectionMetadataChanged> updatedInstances)
            {
                foreach (var evt in updatedInstances)
                {
                    foreach (var registration in _changes)
                    {
                        if (registration.HandlesInstance(evt.InstanceName))
                            registration.Invoke(evt);
                    }
                }
            }

            private ProjectionMetadataChanged BuildEvent(InstanceMetadata metadata, object localObject)
            {
                return new ProjectionMetadataChanged(BuildPublicMetadata(metadata), localObject);
            }

            private static ProjectionInstanceMetadata BuildPublicMetadata(InstanceMetadata metadata)
            {
                return new ProjectionInstanceMetadata(
                    metadata.InstanceName, metadata.Version, metadata.ReaderVersion,
                    metadata.RunningNode, metadata.Status);
            }

            public async Task BuildNewInstance(string instanceName, string nodeName, string version, string minimalReader)
            {
                var updatedInstances = new List<ProjectionMetadataChanged>();
                using (await _lock.Lock())
                {
                    var instance = new InstanceMetadata();
                    instance.InstanceName = instanceName;
                    instance.RunningNode = nodeName;
                    instance.Version = version;
                    instance.ReaderVersion = minimalReader;
                    instance.Status = ProjectionStatus.NewBuild;
                    instance.TokenLock = new AsyncLock();
                    _instances.Add(instance.InstanceName, instance);
                    foreach (var oldInstance in _instances.Values)
                    {
                        if (oldInstance.Status == ProjectionStatus.NewBuild)
                        {
                            oldInstance.Status = ProjectionStatus.CancelledBuild;
                            updatedInstances.Add(BuildEvent(oldInstance, null));
                        }
                    }
                }
                RaiseChanges(updatedInstances);
            }

            public async Task Upgrade(string instanceName, string newVersion)
            {
                var updatedInstances = new List<ProjectionMetadataChanged>();
                using (await _lock.Lock())
                {
                    InstanceMetadata metadata;
                    if (_instances.TryGetValue(instanceName, out metadata))
                    {
                        metadata.Version = newVersion;
                        updatedInstances.Add(BuildEvent(metadata, null));
                    }
                }
                RaiseChanges(updatedInstances);
            }

            public async Task UpdateStatus(string instanceName, ProjectionStatus status)
            {
                var updatedInstances = new List<ProjectionMetadataChanged>();
                using (await _lock.Lock())
                {
                    InstanceMetadata metadata;
                    if (_instances.TryGetValue(instanceName, out metadata))
                    {
                        metadata.Status = status;
                        updatedInstances.Add(BuildEvent(metadata, null));
                    }
                }
                RaiseChanges(updatedInstances);
            }

            public async Task<EventStoreToken> GetToken(string instanceName)
            {
                InstanceMetadata metadata;
                using (await _lock.Lock())
                {
                    if (!_instances.TryGetValue(instanceName, out metadata))
                        return EventStoreToken.Initial;
                }
                using (await metadata.TokenLock.Lock())
                {
                    if (metadata.Token != null)
                        return new EventStoreToken(metadata.Token);
                    var docName = _parent.BuildTokenDocumentName(_projectionName, instanceName);
                    metadata.Token = await _parent._store.GetDocument(docName);
                    return new EventStoreToken(metadata.Token);
                }
            }

            public async Task SetToken(string instanceName, EventStoreToken token)
            {
                InstanceMetadata metadata;
                using (await _lock.Lock())
                {
                    if (!_instances.TryGetValue(instanceName, out metadata))
                        return;
                }
                using (await metadata.TokenLock.Lock())
                {
                    var docName = _parent.BuildTokenDocumentName(_projectionName, instanceName);
                    metadata.Token = token.ToString();
                    await _parent._store.SaveDocument(docName, metadata.Token);
                }
            }

            public async Task<IEnumerable<ProjectionInstanceMetadata>> GetAllMetadata()
            {
                using (await _lock.Lock())
                    return _instances.Values.Select(BuildPublicMetadata).ToList();
            }

            public async Task<ProjectionInstanceMetadata> GetInstanceMetadata(string instanceName)
            {
                using (await _lock.Lock())
                {
                    InstanceMetadata metadata;
                    if (_instances.TryGetValue(instanceName, out metadata))
                        return BuildPublicMetadata(metadata);
                    else
                        return null;
                }
            }

            public IDisposable RegisterForChanges(string instanceName, Contracts.IHandle<ProjectionMetadataChanged> handler)
            {
                var registration = new ChangesRegistration(this, instanceName, handler);
                _changes.Add(registration);
                return registration;
            }

            public void Unregister(ChangesRegistration registration)
            {
                _changes.Remove(registration);
            }
        }

        private class ChangesRegistration : IDisposable
        {
            private ProjectionMetadata _parent;
            private string _instanceName;
            private IHandle<ProjectionMetadataChanged> _handler;

            public ChangesRegistration(ProjectionMetadata parent, string instanceName, IHandle<ProjectionMetadataChanged> handler)
            {
                _parent = parent;
                _instanceName = instanceName;
                _handler = handler;
            }

            public string InstanceName { get { return _instanceName; } }

            public void Invoke(ProjectionMetadataChanged evt)
            {
                _handler.Handle(evt);
            }

            public bool HandlesInstance(string instanceName)
            {
                return _instanceName == null || _instanceName.Equals(instanceName, StringComparison.Ordinal);
            }

            public void Dispose()
            {
                _parent.Unregister(this);
            }
        }
    }
}