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
        void Load(IAggregateId id, Action<T> onLoaded, Action onMissing, Action<Exception> onError);
        void Save(T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError);
    }

    public interface IEventSourcedSerializer
    {
        bool HandlesFormat(string format);
        object Deserialize(EventStoreEvent evt);
        void Serialize(object evt, EventStoreEvent stored);
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

        public void Load(IAggregateId id, Action<T> onLoaded, Action onMissing, Action<Exception> onError)
        {
            new AggregateLoading(this, id, onLoaded, onMissing, onError).Execute();
        }
        public void Save(T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError)
        {
            new AggregateSaving(this, aggregate, onSaved, onConcurrency, onError).Execute();
        }

        private class AggregateLoading
        {
            private EventSourcedRepository<T> _parent;
            private string _streamName;
            private Action<T> _onLoaded;
            private Action _onMissing;
            private Action<Exception> _onError;

            public AggregateLoading(EventSourcedRepository<T> parent, IAggregateId id, Action<T> onLoaded, Action onMissing, Action<Exception> onError)
            {
                _parent = parent;
                _streamName = _parent.StreamNameForId(id);
                _onLoaded = onLoaded;
                _onMissing = onMissing;
                _onError = onError;
            }
            public void Execute()
            {
                _parent._store.ReadStream(_streamName, 0, int.MaxValue, true, EventStreamLoaded, _onError);
            }
            private void EventStreamLoaded(IEventStoreStream stream)
            {
                try
                {
                    if (stream.StreamVersion == 0)
                        _onMissing();
                    else
                    {
                        var deserialized = stream.Events.Select(_parent._serializer.Deserialize).ToList();
                        var aggregate = _parent.CreateAggregate();
                        aggregate.LoadFromEvents(deserialized);
                        aggregate.CommitChanges(stream.StreamVersion);
                        _onLoaded(aggregate);
                    }
                }
                catch (Exception ex)
                {
                    _onError(ex);
                }
            }
        }

        private class AggregateSaving
        {
            private EventSourcedRepository<T> _parent;
            private T _aggregate;
            private Action _onSaved;
            private Action _onConcurrency;
            private Action<Exception> _onError;
            private int _streamVersionForCommit;

            public AggregateSaving(EventSourcedRepository<T> parent, T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError)
            {
                _parent = parent;
                _aggregate = aggregate;
                _onSaved = onSaved;
                _onConcurrency = onConcurrency;
                _onError = onError;
            }
            public void Execute()
            {
                try
                {
                    var changes = _aggregate.GetChanges();
                    if (changes.Count == 0)
                        _onSaved();
                    else
                    {
                        var serialized = new List<EventStoreEvent>(changes.Count);
                        _streamVersionForCommit = _aggregate.OriginalVersion;
                        foreach (var evt in changes)
                        {
                            _streamVersionForCommit++;
                            var stored = new EventStoreEvent();
                            _parent._serializer.Serialize(evt, stored);
                            stored.StreamVersion = _streamVersionForCommit;
                            serialized.Add(stored);
                        }
                        var expectedVersion =
                            _aggregate.OriginalVersion == 0
                            ? EventStoreVersion.New
                            : EventStoreVersion.At(_aggregate.OriginalVersion);
                        var streamName = _parent.StreamNameForId(_aggregate.Id);
                        _parent._store.AddToStream(streamName, serialized, expectedVersion, AggregateSaved, _onConcurrency, _onError);
                    }
                }
                catch (Exception ex)
                {
                    _onError(ex);
                }
            }
            private void AggregateSaved()
            {
                try
                {
                    _aggregate.CommitChanges(_streamVersionForCommit);
                    _onSaved();
                }
                catch (Exception ex)
                {
                    _onError(ex);
                }
            }
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
