using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IEventStore
    {
        void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError);
        void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError);
        void GetAllEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError);
        void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError);
    }

    public interface IEventStoreWaitable : IEventStore
    {
        IDisposable WaitForEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError);
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
        public bool Verify(int streamVersion, string stream = null)
        {
            if (IsNonexistent && streamVersion != 0)
                return false;
            else if (IsNumbered && Version != streamVersion)
                return false;
            else
                return true;
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
    public class ReadStreamComplete : IQueuedExecutionDispatcher
    {
        private Action<IEventStoreStream> _onComplete;
        private EventStoreStream _stream;

        public ReadStreamComplete(Action<IEventStoreStream> onComplete, EventStoreStream stream)
        {
            _onComplete = onComplete;
            _stream = stream;
        }

        public void Execute()
        {
            _onComplete(_stream);
        }
    }
    public class GetAllEventsComplete : IQueuedExecutionDispatcher
    {
        private Action<IEventStoreCollection> _onComplete;
        private IList<EventStoreEvent> _events;
        private EventStoreToken _nextToken;

        public GetAllEventsComplete(Action<IEventStoreCollection> onComplete, IList<EventStoreEvent> events, EventStoreToken nextToken)
        {
            _onComplete = onComplete;
            _events = events;
            _nextToken = nextToken;
        }

        public void Execute()
        {
            _onComplete(new EventStoreCollection(_events, _nextToken));
        }
    }
}
