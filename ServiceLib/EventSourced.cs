using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IAggregateId
    {
    }

    public class AggregateIdGuid : IAggregateId
    {
        public Guid Guid { get; private set; }
        public AggregateIdGuid(Guid id) { Guid = id; }
        public AggregateIdGuid(string id) { Guid = new Guid(id); }
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            if (obj is AggregateIdGuid)
                return Guid.Equals(((AggregateIdGuid)obj).Guid);
            else
                return false;
        }
        public override string ToString()
        {
            return Guid.ToString("N").ToLowerInvariant();
        }

        public static AggregateIdGuid NewGuid()
        {
            return new AggregateIdGuid(Guid.NewGuid());
        }

        public static readonly AggregateIdGuid Empty = new AggregateIdGuid(Guid.Empty);
    }

    public static class AggregateIdGuidExtensions
    {
        public static AggregateIdGuid ToId(this Guid guid)
        {
            return new AggregateIdGuid(guid);
        }
    }

    public interface IEventSourcedAggregate
    {
        IAggregateId Id { get; }
        int OriginalVersion { get; }
        IList<object> GetChanges();
        object CreateSnapshot();
        void CommitChanges(int newVersion);
        int LoadFromSnapshot(object snapshot);
        void LoadFromEvents(IList<object> events);
    }

    public abstract class EventSourcedAggregate : IEventSourcedAggregate
    {
        private List<object> _changes;
        private int _originalVersion;
        private bool _loadingEvents;

        protected EventSourcedAggregate()
        {
            _changes = new List<object>();
        }

        public abstract IAggregateId Id { get; }

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

        protected int CurrentVersion
        {
            get { return _originalVersion + _changes.Count; }
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

        private Dictionary<Type, Action<object>> _eventMapping = new Dictionary<Type, Action<object>>();

        protected void RegisterEventHandlers(IEnumerable<MethodInfo> methods)
        {
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;
                var parameterType = parameters[0].ParameterType;
                var parameterExpr = Expression.Parameter(typeof(object));
                _eventMapping[parameterType] = Expression.Lambda<Action<object>>(
                    Expression.Call(Expression.Constant(this), method, new[] { Expression.Convert(parameterExpr, parameterType) }),
                    parameterExpr
                    ).Compile();
            }
        }

        protected virtual void DispatchEvent(object evt)
        {
            Action<object> handler;
            if (_eventMapping.TryGetValue(evt.GetType(), out handler))
                handler(evt);
        }

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

    public abstract class EventSourcedGuidAggregate : EventSourcedAggregate
    {
        private AggregateIdGuid _id;

        public override IAggregateId Id
        {
            get { return _id; }
        }

        public EventSourcedGuidAggregate()
        {
            _id = AggregateIdGuid.NewGuid();
        }

        protected void SetGuid(Guid id)
        {
            _id = id.ToId();
        }

        protected Guid GetGuid()
        {
            return _id.Guid;
        }
    }

    public interface IEventSourcedRepository<T>
        where T : class, IEventSourcedAggregate
    {
        Task<T> Load(IAggregateId id);
        Task<bool> Save(T aggregate, IEventProcessTrackSource tracker);
    }

    public interface IEventSourcedSerializer
    {
        bool HandlesFormat(string format);
        object Deserialize(EventStoreEvent evt);
        void Serialize(object evt, EventStoreEvent stored);
        object Deserialize(EventStoreSnapshot snapshot);
        void Serialize(object snapshot, EventStoreSnapshot stored);
        string GetTypeName(Type type);
        Type GetTypeFromName(string typeName);
    }

    public abstract class EventSourcedRepository<T>
        : IEventSourcedRepository<T>
        where T : class, IEventSourcedAggregate
    {
        private static readonly EventSourcedRepositoryTraceSource Logger = 
            new EventSourcedRepositoryTraceSource("ServiceLib.EventSourcedRepository." + typeof(T).FullName);

        private IEventStore _store;
        private string _prefix;
        private IEventSourcedSerializer _serializer;
        private int _snapshotInterval;

        protected EventSourcedRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
        {
            _store = store;
            _prefix = prefix;
            _serializer = serializer;
        }

        protected abstract T CreateAggregate();

        protected string Prefix { get { return _prefix; } }

        protected virtual string StreamNameForId(IAggregateId id)
        {
            return new StringBuilder(64)
                .Append(Prefix)
                .Append('_')
                .Append(id.ToString())
                .ToString();
        }

        protected virtual bool ShouldCreateSnapshot(T aggregate)
        {
            if (_snapshotInterval <= 0)
                return false;
            var fromLastInterval = aggregate.OriginalVersion % _snapshotInterval + aggregate.GetChanges().Count;
            return fromLastInterval >= _snapshotInterval;
        }
        public int SnapshotInterval
        {
            get { return _snapshotInterval; }
            set { _snapshotInterval = value; }
        }

        public async Task<T> Load(IAggregateId id)
        {
            var streamName = StreamNameForId(id);

            var storedSnapshot = await _store.LoadSnapshot(streamName);
            var fromVersion = 0;
            var snapshot = storedSnapshot == null ? null : _serializer.Deserialize(storedSnapshot);
            T aggregate = null;

            if (snapshot != null)
            {
                aggregate = CreateAggregate();
                fromVersion = 1 + aggregate.LoadFromSnapshot(snapshot);
            }

            var stream = await _store.ReadStream(streamName, fromVersion, int.MaxValue, true);
            if (stream.StreamVersion != 0)
            {
                var deserialized = stream.Events.Select(_serializer.Deserialize).ToList();
                if (aggregate == null)
                    aggregate = CreateAggregate();
                aggregate.LoadFromEvents(deserialized);
                aggregate.CommitChanges(stream.StreamVersion);
            }

            return aggregate;
        }

        public async Task<bool> Save(T aggregate, IEventProcessTrackSource tracker)
        {
            var changes = aggregate.GetChanges();
            if (changes.Count != 0)
            {
                var serialized = new List<EventStoreEvent>(changes.Count);
                var streamVersionForCommit = aggregate.OriginalVersion;
                foreach (var evt in changes)
                {
                    streamVersionForCommit++;
                    var stored = new EventStoreEvent();
                    _serializer.Serialize(evt, stored);
                    stored.StreamVersion = streamVersionForCommit;
                    serialized.Add(stored);
                }
                var expectedVersion =
                    aggregate.OriginalVersion == 0
                    ? EventStoreVersion.New
                    : EventStoreVersion.At(aggregate.OriginalVersion);
                var streamName = StreamNameForId(aggregate.Id);
                var addedToStream = await _store.AddToStream(streamName, serialized, expectedVersion);
                if (!addedToStream)
                {
                    return false;
                }

                bool shouldCreateSnapshot = ShouldCreateSnapshot(aggregate);
                aggregate.CommitChanges(streamVersionForCommit);
                if (shouldCreateSnapshot)
                {
                    var objectSnapshot = aggregate.CreateSnapshot();
                    if (objectSnapshot != null)
                    {
                        var storedSnapshot = new EventStoreSnapshot();
                        _serializer.Serialize(objectSnapshot, storedSnapshot);
                        await _store.SaveSnapshot(streamName, storedSnapshot);
                    }
                }

                foreach (var stored in serialized)
                    tracker.AddEvent(stored.Token);
            }
            return true;
        }
    }

    public class EventSourcedRepositoryDefault<T>
        : EventSourcedRepository<T>
        where T : class, IEventSourcedAggregate, new()
    {
        public EventSourcedRepositoryDefault(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override T CreateAggregate()
        {
            return new T();
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
            stored.Format = "json";
            stored.Body = JsonSerializer.SerializeToString(evt, type);
        }

        public object Deserialize(EventStoreSnapshot snapshot)
        {
            return JsonSerializer.DeserializeFromString(snapshot.Body, _mapper.GetType(snapshot.Type));
        }

        public void Serialize(object snapshot, EventStoreSnapshot stored)
        {
            var type = snapshot.GetType();
            stored.Type = _mapper.GetName(type);
            stored.Format = "json";
            stored.Body = JsonSerializer.SerializeToString(snapshot, type);
        }

        public bool HandlesFormat(string format)
        {
            return format == "json";
        }

        public string GetTypeName(Type type)
        {
            return _mapper.GetName(type);
        }

        public Type GetTypeFromName(string typeName)
        {
            return _mapper.GetType(typeName);
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
