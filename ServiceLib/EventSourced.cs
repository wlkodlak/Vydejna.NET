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
        Task<bool> Save(T aggregate);
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

        public Task<T> Load(IAggregateId id)
        {
            var context = new LoadContext<T>(StreamNameForId(id));
            return _store.LoadSnapshot(context.StreamName)
                .ContinueWith<Task<IEventStoreStream>>(Load_ProcessSnapshot, context).Unwrap()
                .ContinueWith<T>(Load_ProcessEvents, context);
        }

        private class LoadContext<T>
        {
            public readonly string StreamName;
            public T Aggregate;

            public LoadContext(string streamName)
            {
                StreamName = streamName;
            }
        }

        private Task<IEventStoreStream> Load_ProcessSnapshot(Task<EventStoreSnapshot> task, object objContext)
        {
            var context = (LoadContext<T>)objContext;
            var storedSnapshot = task.Result;
            var fromVersion = 0;
            var snapshot = storedSnapshot == null ? null : _serializer.Deserialize(storedSnapshot);
            if (snapshot != null)
            {
                context.Aggregate = CreateAggregate();
                fromVersion = 1 + context.Aggregate.LoadFromSnapshot(snapshot);
            }
            return _store.ReadStream(context.StreamName, fromVersion, int.MaxValue, true);
        }

        private T Load_ProcessEvents(Task<IEventStoreStream> task, object objContext)
        {
            var context = (LoadContext<T>)objContext;
            var stream = task.Result;
            if (stream.StreamVersion == 0)
                return default(T);
            else
            {
                var deserialized = stream.Events.Select(_serializer.Deserialize).ToList();
                if (context.Aggregate == null)
                    context.Aggregate = CreateAggregate();
                context.Aggregate.LoadFromEvents(deserialized);
                context.Aggregate.CommitChanges(stream.StreamVersion);
                return context.Aggregate;
            }
        }

        public Task<bool> Save(T aggregate)
        {
            return TaskUtils.FromEnumerable<bool>(SaveInternal(aggregate)).GetTask();
        }

        private IEnumerable<Task> SaveInternal(T aggregate)
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
                var taskAddToStream = _store.AddToStream(streamName, serialized, expectedVersion);
                yield return taskAddToStream;
                if (!taskAddToStream.Result)
                {
                    yield return Task.FromResult(false);
                    yield break;
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
                        var taskSaveSnapshot = _store.SaveSnapshot(streamName, storedSnapshot);
                        yield return taskSaveSnapshot;
                    }
                }
            }
            yield return Task.FromResult(true);
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
