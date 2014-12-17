using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        Task<IEventStoreCollection> WaitForEvents(
            EventStoreToken token,
            int maxCount,
            bool loadBody,
            CancellationToken cancel);
    }

    public class EventStoreVersion
    {
        private readonly int _version;

        private EventStoreVersion(int version)
        {
            _version = version;
        }

        private static readonly EventStoreVersion _any = new EventStoreVersion(-1);
        private static readonly EventStoreVersion _new = new EventStoreVersion(0);

        public static EventStoreVersion Any
        {
            get { return _any; }
        }

        public static EventStoreVersion New
        {
            get { return _new; }
        }

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
        private static readonly EventStoreToken _initial = new EventStoreToken(string.Empty, 1);
        private static readonly EventStoreToken _current = new EventStoreToken(string.Empty, 2);

        public static EventStoreToken Initial
        {
            get { return _initial; }
        }

        public static EventStoreToken Current
        {
            get { return _current; }
        }

        private readonly string _token;
        private readonly int _mode;

        public EventStoreToken(string token)
        {
            _token = token ?? string.Empty;
            _mode = string.IsNullOrEmpty(_token) ? 1 : 0;
        }

        private EventStoreToken(string token, int mode)
        {
            _token = token;
            _mode = mode;
        }

        public bool IsInitial
        {
            get { return _mode == 1; }
        }

        public bool IsCurrent
        {
            get { return _mode == 2; }
        }

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

    public class EventStoreTraceSource : TraceSource
    {
        public EventStoreTraceSource(string name)
            : base(name)
        {
        }

        public void AddToStreamComplete(string stream, List<EventStoreEvent> newEvents)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 1, "{EventsCount} events added to stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("EventsCount", false, newEvents);
            msg.Log(this);
        }

        public void AddToStreamConflicts(string stream, int streamVersion, EventStoreVersion expectedVersion)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 2, "Concurrency conflict occurred when adding events to stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("ExpectedVersion", false, expectedVersion);
            msg.SetProperty("ActualVersion", false, streamVersion);
            msg.Log(this);
        }

        public void AddToStreamFailed(string stream, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 3, "Failed to add event to stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ReadFromStreamComplete(string stream, int minVersion, int maxCount, int streamVersion, List<EventStoreEvent> list)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 5, "{EventsCount} events read from stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("MinVersion", false, minVersion);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("StreamVersion", false, streamVersion);
            msg.SetProperty("EventsCount", false, list.Count);
            msg.Log(this);
        }

        public void ReadFromStreamFailed(string stream, int minVersion, int maxCount, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 6, "Failed to read events from stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("MinVersion", false, minVersion);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void LoadBodiesFinished(int eventsLoaded)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 8, "Loaded bodies for {EventsCount} events");
            msg.SetProperty("EventsCount", false, eventsLoaded);
            msg.Log(this);
        }

        public void LoadSnapshotFinished(string stream, EventStoreSnapshot snapshot)
        {
            var snapshotExists = snapshot != null;
            var msg = new LogContextMessage(TraceEventType.Verbose, 21,
                snapshotExists ? "Loaded snapshot for stream {StreamName}" : "No snapshot loaded for stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("SnapshotExists", false, snapshotExists);
            msg.Log(this);
        }

        public void SaveSnapshotFinished(string stream, EventStoreSnapshot snapshot)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 22, "Saved snapshot for stream {StreamName}");
            msg.SetProperty("StreamName", false, stream);
            msg.Log(this);
        }    
    }

    public class EventStoreInMemoryTraceSource : EventStoreTraceSource
    {
        public EventStoreInMemoryTraceSource(string name)
            : base(name)
        {
        }

        public void GetAllEventsFailed(EventStoreToken token, int maxCount, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 14, "Failed to get events from token {Token} (without waiting)");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void WaitForEventsFailed(EventStoreToken token, int maxCount, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 15, "Failed to get events from token {Token} (with waiting)");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void WaitForEventsCancelled(int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 13, "Cancelled waiting for new events");
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void WaitForEventsInitialized(EventStoreToken token, int skip, int maxCount, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 11, "Initializing WaitForEvents from token {Token}");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Skip", false, skip);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void WaitForEventsComplete(EventStoreToken token, int skip, int maxCount, int taskId, List<EventStoreEvent> readyEvents, EventStoreToken nextToken)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 12, "Finished WaitForEvents from token {Token}, returning {EventsCount} events");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Skip", false, skip);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("TaskId", false, taskId);
            msg.SetProperty("EventsCount", false, readyEvents.Count);
            msg.SetProperty("NextToken", false, nextToken);
            msg.Log(this);
        }
    }

    public class EventStorePostgresTraceSource : EventStoreTraceSource
    {
        public EventStorePostgresTraceSource(string name)
            : base(name)
        {
        }

        public void GotStreamVersion(string stream, int version)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 30, "Stream {StreamName} has version {Version}");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("Version", false, version);
            msg.Log(this);
        }

        public void StreamCreated(string stream)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 41, "Stream {StreamName} created");
            msg.SetProperty("StreamName", false, stream);
            msg.Log(this);
        }

        public void StreamCreationConflicted(string stream)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 42, "Stream {StreamName} already existed when trying to create it");
            msg.SetProperty("StreamName", false, stream);
            msg.Log(this);
        }

        public void StreamCreationFailed(string stream, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 43, "Stream {StreamName} creation failed");
            msg.SetProperty("StreamName", false, stream);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void InsertedEvent(string streamName, int streamVersion, EventStoreToken token)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 35, "Inserted event #{StreamVersion} to stream {StreamName}");
            msg.SetProperty("StreamName", false, streamName);
            msg.SetProperty("StreamVersion", false, streamVersion);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void SnapshotFound(string streamName, bool snapshotExists)
        {
            var summary = snapshotExists ? "Snapshot for stream {StreamName} found" : "Snapshot for stream {StreamName} not found";
            var msg = new LogContextMessage(TraceEventType.Verbose, 51, summary);
            msg.SetProperty("StreamName", false, streamName);
            msg.SetProperty("Found", false, snapshotExists);
            msg.Log(this);
        }

        public void SnapshotInserted(EventStoreSnapshot snapshot)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 52, "Snapshot for stream {StreamName} inserted");
            msg.SetProperty("StreamName", false, snapshot.StreamName);
            msg.Log(this);
        }

        public void SnapshotUpdated(EventStoreSnapshot snapshot)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 53, "Snapshot for stream {StreamName} updated");
            msg.SetProperty("StreamName", false, snapshot.StreamName);
            msg.Log(this);
        }

        public void SnapshotInsertConflicted(EventStoreSnapshot snapshot)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 54, "Snapshot for stream {StreamName} was already present when inserting");
            msg.SetProperty("StreamName", false, snapshot.StreamName);
            msg.Log(this);
        }

        public void SaveSnapshotFailed(string streamName, EventStoreSnapshot snapshot, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 59, "Saving snapshot failed");
            msg.SetProperty("StreamName", false, streamName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

    }
}
