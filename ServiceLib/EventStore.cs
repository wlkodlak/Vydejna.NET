using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventStore
    {
        Task<bool> AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion);
        Task<IEventStoreStream> ReadStream(string stream, int minVersion, int maxCount, bool loadBody);
        Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody);
        Task LoadBodies(IList<EventStoreEvent> events);
        Task<EventStoreSnapshot> LoadSnapshot(string stream);
        Task SaveSnapshot(string stream, EventStoreSnapshot snapshot);
    }

    public interface IEventStoreWaitable : IEventStore
    {
        Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel);
    }

    public class EventStoreVersion
    {
        private int _version;
        private EventStoreVersion(int version)
        {
            _version = version;
        }
        private static EventStoreVersion _any = new EventStoreVersion(-1);
        private static EventStoreVersion _new = new EventStoreVersion(0);

        public static EventStoreVersion Any { get { return _any; } }
        public static EventStoreVersion New { get { return _new; } }
        public static EventStoreVersion At(int version)
        {
            return new EventStoreVersion(version);
        }

        public override bool Equals(object obj)
        {
            var oth = obj as EventStoreVersion;
            if (ReferenceEquals(this, oth))
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
            if (_version < 0)
                return "Any";
            else if (_version == 0)
                return "New";
            else
                return string.Format("Version {0}", _version);
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
        public bool VerifyVersion(int streamVersion)
        {
            if (_version < 0)
                return true;
            else
                return _version == streamVersion;
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
        public override string ToString()
        {
            return string.Concat(
                "#", Token.ToString(),
                " ", Type,
                " ", StreamName,
                "@", StreamVersion.ToString());
        }
    }
    public class EventStoreSnapshot
    {
        public string StreamName { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public string Body { get; set; }
    }
    public class EventStoreToken : IComparable<EventStoreToken>, IEquatable<EventStoreToken>
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
            return Equals(obj as EventStoreToken);
        }
        public override int GetHashCode()
        {
            return _token.GetHashCode();
        }
        public override string ToString()
        {
            return _token;
        }
        public bool Equals(EventStoreToken oth)
        {
            return oth != null && _token.Equals(oth._token, StringComparison.Ordinal) && _mode == oth._mode;
        }
        public int CompareTo(EventStoreToken other)
        {
            return Compare(this, other);
        }
        public static int Compare(EventStoreToken a, EventStoreToken b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null) ? 0 : -1;
            else if (ReferenceEquals(b, null))
                return 1;
            else if (a._mode == 0)
            {
                if (b._mode == 0)
                {
                    if (a._token.Length == b._token.Length)
                        return string.CompareOrdinal(a._token, b._token);
                    else
                    {
                        var length = Math.Max(a._token.Length, b._token.Length);
                        var tokenA = a._token.PadLeft(length, '0');
                        var tokenB = b._token.PadLeft(length, '0');
                        return string.CompareOrdinal(tokenA, tokenB);
                    }
                }
                else
                    return b._mode == 1 ? 1 : -1;
            }
            else if (b._mode == 0)
                return a._mode == 1 ? -1 : 1;
            else
                return (a._mode == b._mode) ? 0 : (a._mode == 1) ? -1 : 1;
        }
    }
    public interface IEventStoreCollection
    {
        EventStoreToken NextToken { get; }
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
        public EventStoreCollection(IEnumerable<EventStoreEvent> events, EventStoreToken next)
        {
            _events = events.ToList();
            _nextToken = next;
        }
        public EventStoreToken NextToken
        {
            get { return _nextToken; }
        }
        public IList<EventStoreEvent> Events
        {
            get { return _events; }
        }
    }
    public class EventStoreStream : IEventStoreStream
    {
        private readonly int _version;
        private readonly int _hint;
        private readonly IList<EventStoreEvent> _events;
        public EventStoreStream(IList<EventStoreEvent> events, int version, int hint)
        {
            _events = events;
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
}
