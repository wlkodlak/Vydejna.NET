using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IEventSourcedAggregate
    {
        Guid Id { get; }
        int OriginalVersion { get; }
        IList<object> GetChanges();
        object CreateSnapshot();
        void CommitChanges(int newVersion);
        int LoadFromSnapshot(object snapshot);
        void LoadFromEvents(IList<object> events);
    }

    public abstract class EventSourcedAggregate : IEventSourcedAggregate
    {
        private Guid _id;
        private List<object> _changes;
        private int _originalVersion;
        private bool _loadingEvents;

        protected EventSourcedAggregate()
        {
            _changes = new List<object>();
        }

        public Guid Id
        {
            get { return _id; }
            protected set { _id = value; }
        }

        int IEventSourcedAggregate.OriginalVersion
        {
            get { return _originalVersion; }
        }

        IList<object> IEventSourcedAggregate.GetChanges()
        {
            return _changes;
        }

        void IEventSourcedAggregate.CommitChanges(int newVersion)
        {
            _originalVersion = newVersion;
            _changes.Clear();
        }

        protected virtual object CreateSnapshot()
        {
            return null;
        }

        protected virtual int LoadFromSnapshot(object snapshot)
        {
            return 0;
        }

        protected void RecordChange(object ev)
        {
            if (!_loadingEvents)
                _changes.Add(ev);
        }

        protected abstract void DispatchEvent(object evt);

        void IEventSourcedAggregate.LoadFromEvents(IList<object> events)
        {
            _loadingEvents = true;
            foreach (var evt in events)
                DispatchEvent(evt);
            _loadingEvents = false;
        }

        object IEventSourcedAggregate.CreateSnapshot()
        {
            return CreateSnapshot();
        }

        int IEventSourcedAggregate.LoadFromSnapshot(object snapshot)
        {
            return LoadFromSnapshot(snapshot);
        }
    }

    public interface IEventSourcedRepository<T>
        where T : class, IEventSourcedAggregate
    {
        Task<T> Get(Guid id);
        Task Save(T aggregate);
    }

    public interface IEventSourcedSerializer
    {
        object Deserialize(EventStoreEvent evt);
        void Serialize(object evt, EventStoreEvent stored);
    }

    public abstract class EventSourcedRepository<T>
        : IEventSourcedRepository<T>
        where T : class, IEventSourcedAggregate
    {
        private IEventStore _store;
        private string _prefix;
        private IEventSourcedSerializer _serializer;

        protected EventSourcedRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
        {
            _store = store;
            _prefix = prefix;
            _serializer = serializer;
        }

        protected abstract T CreateAggregate();

        public async Task<T> Get(Guid id)
        {
            var storedEvents = await _store.ReadStream(StreamNameForId(id), 0, int.MaxValue, true);
            if (storedEvents.StreamVersion == 0)
                return null;
            var deserialized = storedEvents.Events.Select(_serializer.Deserialize).ToList();
            var aggregate = CreateAggregate();
            aggregate.LoadFromEvents(deserialized);
            aggregate.CommitChanges(storedEvents.StreamVersion);
            return aggregate;
        }

        protected string Prefix { get { return _prefix; } }

        protected virtual string StreamNameForId(Guid id)
        {
            return new StringBuilder(64)
                .Append(Prefix)
                .Append('_')
                .Append(id.ToString("N").ToLowerInvariant())
                .ToString();
        }

        public async Task Save(T aggregate)
        {
            var changes = aggregate.GetChanges();
            if (changes.Count == 0)
                return;
            var serialized = new List<EventStoreEvent>(changes.Count);
            foreach (var evt in changes)
            {
                var stored = new EventStoreEvent();
                _serializer.Serialize(evt, stored);
                serialized.Add(stored);
            }
            var expectedVersion = aggregate.OriginalVersion == 0 ? EventStoreVersion.EmptyStream : EventStoreVersion.Number(aggregate.OriginalVersion);
            await _store.AddToStream(StreamNameForId(aggregate.Id), serialized, expectedVersion);
            aggregate.CommitChanges(serialized.Last().StreamVersion);
        }
    }

    public class EventSourcedJsonSerializer : IEventSourcedSerializer
    {
        private ITypeMapper _mapper;

        public EventSourcedJsonSerializer(ITypeMapper mapper)
        {
            _mapper = mapper;
        }

        public object Deserialize(EventStoreEvent evt)
        {
            return JsonSerializer.DeserializeFromString(evt.Body, _mapper.GetType(evt.Type));
        }

        public void Serialize(object evt, EventStoreEvent stored)
        {
            var type = evt.GetType();
            stored.Type = _mapper.GetName(type);
            stored.Body = JsonSerializer.SerializeToString(evt, type);
        }
    }

    [Serializable]
    public class ValidationException : Exception
    {
        public ValidationException() { }
        public ValidationException(string message) : base(message) { }
        public ValidationException(string message, Exception inner) : base(message, inner) { }
        protected ValidationException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }
    }
}
