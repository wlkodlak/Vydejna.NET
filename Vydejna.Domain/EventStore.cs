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
        void WaitForEvents(EventStoreToken token, CancellationToken cancel, Action onComplete, Action<Exception> onError);
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
    public class EventStoreWaitable : IEventStoreWaitable
    {
        private IEventStore _store;
        private IEventStoreWaitable _waitable;
        private ITime _time;
        private int _timeout = 200;

        public EventStoreWaitable(IEventStore store, ITime time)
        {
            _store = store;
            _waitable = store as IEventStoreWaitable;
            _time = time;
        }

        public EventStoreWaitable WithTimeout(int timeout)
        {
            _timeout = Math.Max(1, timeout);
            return this;
        }

        public void WaitForEvents(EventStoreToken token, CancellationToken cancel, Action onComplete, Action<Exception> onError)
        {
            if (_waitable != null)
                _waitable.WaitForEvents(token, cancel, onComplete, onError);
            else
                _time.Delay(_timeout, cancel, onComplete);
        }

        public void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
        {
            _store.AddToStream(stream, events, expectedVersion, onComplete, onConcurrency, onError);
        }

        public void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
        {
            _store.ReadStream(stream, minVersion, maxCount, loadBody, onComplete, onError);
        }

        public void GetAllEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            _store.GetAllEvents(token, streamPrefix, eventType, maxCount, loadBody, onComplete, onError);
        }

        public void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
        {
            _store.LoadBodies(events, onComplete, onError);
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

        public void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
        {
            try
            {
                bool wasCompleted;
                using (_lock.Update())
                {
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    wasCompleted = expectedVersion.Verify(streamVersion, stream);
                    if (wasCompleted)
                    {
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
                    }
                }
                if (wasCompleted)
                    onComplete();
                else
                    onConcurrency();
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }

        public void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
        {
            try
            {
                EventStoreStream result;
                using (_lock.Read())
                {
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    var list = _events.Where(e => e.StreamName == stream && e.StreamVersion >= minVersion).Take(maxCount).ToList();
                    result = new EventStoreStream(list, streamVersion, 0);
                }
                onComplete(result);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }

        public void GetAllEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            EventStoreCollection output;
            using (_lock.Read())
            {
                int skip = token.IsInitial ? 0 : token.IsCurrent ? _events.Count : int.Parse(token.ToString()) + 1;
                if (_events.Count == 0)
                    output = new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), token);
                else if (skip >= _events.Count)
                    output = new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), _events.Last().Token);
                else if (maxCount <= 0)
                    output = new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), token);
                else
                {
                    int counter = maxCount;
                    var events = new List<EventStoreEvent>();
                    var next = (EventStoreToken)null;
                    for (int idx = skip; idx < _events.Count && counter > 0; idx++)
                    {
                        var evt = _events[idx];
                        if (evt.StreamName.StartsWith(streamPrefix ?? "") && (eventType == null || evt.Type == eventType))
                        {
                            events.Add(evt);
                            counter--;
                        }
                        next = evt.Token;
                    }
                    output = new EventStoreCollection(events, next);
                }
            }
            onComplete(output);
        }

        public void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
        {
            onComplete();
        }

    }
}
