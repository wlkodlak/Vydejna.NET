using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IEventStore
    {
        Task AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion);
        Task<IEventStoreStream> ReadStream(string stream, int minVersion = 0, int maxCount = int.MaxValue, bool loadBody = true);
        Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount = int.MaxValue, bool loadBody = false);
        Task LoadBodies(IList<EventStoreEvent> events);
    }

    public interface IEventStoreWaitable : IEventStore
    {
        Task WaitForEvents(EventStoreToken token, int timeout = 0);
    }

    public class EventStoreVersion
    {
        private int _version;
        private EventStoreVersion(int version)
        {
            _version = version;
        }
        private static EventStoreVersion _any = new EventStoreVersion(-2);
        private static EventStoreVersion _empty = new EventStoreVersion(-1);

        public static EventStoreVersion Any { get { return _any; } }
        public static EventStoreVersion EmptyStream { get { return _empty; } }
        public static EventStoreVersion Number(int version)
        {
            return new EventStoreVersion(version);
        }

        public int Version { get { return _version; } }
        public bool IsNumbered { get { return _version >= 0; } }
        public bool IsNonexistent { get { return _version == -1; } }
        public bool IsAny { get { return _version == -2; } }

        public override bool Equals(object obj)
        {
            var oth = obj as EventStoreVersion;
            if (ReferenceEquals(obj, oth))
                return true;
            else if (ReferenceEquals(oth, null))
                return false;
            else
                return _version == oth._version;
        }
        public override int GetHashCode()
        {
            return _version;
        }
        public override string ToString()
        {
            if (IsNumbered)
                return string.Format("Version {0}", _version);
            else if (IsAny)
                return "Any version";
            else if (IsNonexistent)
                return "Nonexistent";
            else
                return "Invalid version";
        }
        public static bool operator ==(EventStoreVersion a, EventStoreVersion b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);
            else
                return a.Equals(b);
        }
        public static bool operator !=(EventStoreVersion a, EventStoreVersion b)
        {
            return !(a == b);
        }
        public void Verify(int streamVersion, string stream = null)
        {
            if (IsNonexistent && streamVersion != 0)
                throw new InvalidOperationException(string.Format("Stream {0} is in version {1}, it was not supposed to exist.", stream ?? "", streamVersion));
            else if (IsNumbered && Version != streamVersion)
                throw new InvalidOperationException(string.Format("Stream {0} is in version {1}, expecting {2}", stream ?? "", streamVersion, Version));
        }
    }
    public class EventStoreEvent
    {
        public EventStoreToken Token { get; set; }
        public string StreamName { get; set; }
        public int StreamVersion { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public string Body { get; set; }
    }
    public class EventStoreToken
    {
        private static EventStoreToken _initial = new EventStoreToken { _token = string.Empty, _mode = 1 };
        private static EventStoreToken _current = new EventStoreToken { _token = string.Empty, _mode = 2 };
        public static EventStoreToken Initial { get { return _initial; } }
        public static EventStoreToken Current { get { return _current; } }

        private string _token;
        private int _mode;

        private EventStoreToken()
        {
        }
        public EventStoreToken(string token)
        {
            _token = token ?? string.Empty;
            _mode = string.IsNullOrEmpty(_token) ? 1 : 0;
        }
        public bool IsInitial { get { return _mode == 1; } }
        public bool IsCurrent { get { return _mode == 2; } }

        public override bool Equals(object obj)
        {
            var oth = obj as EventStoreToken;
            return oth != null && _token.Equals(oth._token, StringComparison.Ordinal) && _mode == oth._mode;
        }
        public override int GetHashCode()
        {
            return _token.GetHashCode();
        }
        public override string ToString()
        {
            return _token;
        }
    }
    public interface IEventStoreCollection
    {
        EventStoreToken NextToken { get; }
        bool HasMoreEvents { get; }
        IList<EventStoreEvent> Events { get; }
    }
    public interface IEventStoreStream
    {
        int StreamVersion { get; }
        int PreviousEventsHint { get; }
        IList<EventStoreEvent> Events { get; }
    }
    public class EventStoreCollection : IEventStoreCollection
    {
        private readonly EventStoreToken _nextToken;
        private readonly List<EventStoreEvent> _events;
        private readonly bool _hasMoreEvents;
        public EventStoreCollection(IEnumerable<EventStoreEvent> events, EventStoreToken next, bool more)
        {
            _events = events.ToList();
            _nextToken = next;
            _hasMoreEvents = more;
        }
        public EventStoreToken NextToken
        {
            get { return _nextToken; }
        }
        public IList<EventStoreEvent> Events
        {
            get { return _events; }
        }
        public bool HasMoreEvents
        {
            get { return _hasMoreEvents; }
        }
    }
    public class EventStoreStream : IEventStoreStream
    {
        private readonly int _version;
        private readonly int _hint;
        private List<EventStoreEvent> _events;
        public EventStoreStream(List<EventStoreEvent> events, int version, int hint)
        {
            _events = events.ToList();
            _version = version;
            _hint = hint;
        }
        public int StreamVersion
        {
            get { return _version; }
        }
        public int PreviousEventsHint
        {
            get { return _hint; }
        }
        public IList<EventStoreEvent> Events
        {
            get { return _events; }
        }
    }
    public class EventStoreWaitable : IEventStoreWaitable
    {
        private IEventStore _store;
        private IEventStoreWaitable _waitable;
        private ITime _time;
          
        public EventStoreWaitable(IEventStore store, ITime time)
        {
            _store = store;
            _waitable = store as IEventStoreWaitable;
            _time = time;
        }

        public Task WaitForEvents(EventStoreToken token, int timeout = 0)
        {
            if (_waitable != null)
                return _waitable.WaitForEvents(token, timeout);
            else if (timeout <= 0)
                return _time.Delay(200);
            else
                return _time.Delay(Math.Min(200, timeout));
        }

        public Task AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            return _store.AddToStream(stream, events, expectedVersion);
        }

        public Task<IEventStoreStream> ReadStream(string stream, int minVersion = 0, int maxCount = int.MaxValue, bool loadBody = true)
        {
            return _store.ReadStream(stream, minVersion, maxCount, loadBody);
        }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount = int.MaxValue, bool loadBody = false)
        {
            return _store.GetAllEvents(token, maxCount, loadBody);
        }

        public Task LoadBodies(IList<EventStoreEvent> events)
        {
            return _store.LoadBodies(events);
        }
    }

    public class EventStoreInMemory : IEventStore
    {
        private List<EventStoreEvent> _events;
        private UpdateLock _lock = new UpdateLock();
        private Dictionary<string, int> _versions;

        public EventStoreInMemory()
        {
            _events = new List<EventStoreEvent>();
            _versions = new Dictionary<string, int>();
        }

        public Task AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            using (_lock.Update())
            {
                int streamVersion;
                _versions.TryGetValue(stream, out streamVersion);
                expectedVersion.Verify(streamVersion, stream);

                var newEvents = events.ToList();
                var token = _events.Count;
                foreach (var evt in newEvents)
                {
                    evt.StreamName = stream;
                    evt.StreamVersion = ++streamVersion;
                    evt.Token = new EventStoreToken(token.ToString());
                    token++;
                }

                _lock.Write();
                _versions[stream] = streamVersion;
                _events.AddRange(newEvents);

                return TaskResult.GetCompletedTask();
            }
        }

        public Task<IEventStoreStream> ReadStream(string stream, int minVersion = 0, int maxCount = int.MaxValue, bool loadBody = true)
        {
            using (_lock.Read())
            {
                int streamVersion;
                _versions.TryGetValue(stream, out streamVersion);
                var list = _events.Where(e => e.StreamName == stream && e.StreamVersion >= minVersion).Take(maxCount).ToList();
                var result = new EventStoreStream(list, streamVersion, 0);
                return TaskResult.GetCompletedTask<IEventStoreStream>(result);
            }
        }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount = int.MaxValue, bool loadBody = false)
        {
            using (_lock.Read())
            {
                var result = Enumerable.Empty<EventStoreEvent>();
                var next = token;
                var more = false;
                int skip = token.IsInitial ? 0 : token.IsCurrent ? _events.Count : int.Parse(token.ToString());

                if (_events.Count == 0)
                    token = EventStoreToken.Initial;
                else if (maxCount <= 0)
                    more = skip < _events.Count;
                else if (skip < _events.Count)
                {
                    var list = _events.Skip(skip).Take(maxCount).ToList();
                    result = list;
                    next = result.Last().Token;
                    more = (skip + list.Count) < _events.Count;
                }
                else
                    next = _events.Last().Token;

                return TaskResult.GetCompletedTask<IEventStoreCollection>(new EventStoreCollection(result, next, more));
            }
        }

        public Task LoadBodies(IList<EventStoreEvent> events)
        {
            return TaskResult.GetCompletedTask();
        }

    }
}
