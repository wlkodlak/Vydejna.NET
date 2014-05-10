using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Threading;
using System.Collections.Concurrent;

namespace ServiceLib
{
    public class EventStorePostgres : IEventStoreWaitable, IDisposable
    {
        private DatabasePostgres _db;
        private ITime _time;
        private static EventStoreEvent[] EmptyList = new EventStoreEvent[0];
        private int _waiterId;
        private ConcurrentDictionary<int, WaitForEventsContext> _waiters;
        private AutoResetEventAsync _notified;
        private bool _disposed;
        private int _canStartNotifications;
        private CancellationTokenSource _cancelListening;
        private Task _notificationTask;

        public EventStorePostgres(DatabasePostgres db, ITime time)
        {
            _db = db;
            _time = time;
            _waiters = new ConcurrentDictionary<int, WaitForEventsContext>();
            _notified = new AutoResetEventAsync();
            _canStartNotifications = 1;
            _cancelListening = new CancellationTokenSource();
        }

        private void StartNotifications()
        {
            if (Interlocked.Exchange(ref _canStartNotifications, 0) == 1)
            {
                _db.Listen("eventstore", s => _notified.Set(), _cancelListening.Token);
                _notificationTask = TaskUtils.FromEnumerable(WaitForEvents_CoreInternal()).Catch<Exception>(ex => true).GetTask();
            }
        }

        public void Initialize()
        {
            _db.ExecuteSync(InitializeDatabase);
        }

        public void Dispose()
        {
            _cancelListening.Cancel();
            _canStartNotifications = 0;
            _disposed = true;
            _notified.Set();
            if (_notificationTask != null)
                _notificationTask.Wait(1000);
            _cancelListening.Dispose();
        }

        private void InitializeDatabase(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS eventstore_streams (" +
                    "streamname varchar PRIMARY KEY, " +
                    "version integer NOT NULL" +
                    ")";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS eventstore_events (" +
                    "id bigserial PRIMARY KEY, " +
                    "streamname varchar NOT NULL, " +
                    "version integer NOT NULL, " +
                    "format varchar, eventtype varchar, contents text, " +
                    "UNIQUE (streamname, version)" +
                    ")";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS eventstore_snapshots (" +
                    "streamname varchar PRIMARY KEY, " +
                    "format varchar, snapshottype varchar, contents text" +
                    ")";
                cmd.ExecuteNonQuery();
            }
        }

        public Task<bool> AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            return _db.Query(AddToStreamWorker, new AddToStreamParameters(stream, events, expectedVersion));
        }

        private class AddToStreamParameters
        {
            public readonly string Stream;
            public readonly IEnumerable<EventStoreEvent> Events;
            public readonly EventStoreVersion ExpectedVersion;

            public AddToStreamParameters(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
            {
                Stream = stream;
                Events = events;
                ExpectedVersion = expectedVersion;
            }
        }

        private bool AddToStreamWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (AddToStreamParameters)objContext;
            using (var tran = conn.BeginTransaction())
            {
                var rawVersion = GetStreamVersion(conn, context.Stream);
                var realVersion = rawVersion == -1 ? 0 : rawVersion;
                if (!context.ExpectedVersion.VerifyVersion(realVersion))
                    return false;
                if (rawVersion == -1)
                {
                    if (CreateStream(conn, tran, context.Stream))
                    {
                        rawVersion = GetStreamVersion(conn, context.Stream);
                        realVersion = rawVersion == -1 ? 0 : rawVersion;
                        if (!context.ExpectedVersion.VerifyVersion(realVersion))
                            return false;
                    }
                }
                var events = context.Events.ToList();
                InsertNewEvents(conn, context.Stream, realVersion, events);
                UpdateStreamVersion(conn, context.Stream, realVersion + events.Count);
                NotifyChanges(conn);
                tran.Commit();
                return true;
            }
        }

        private static int GetStreamVersion(NpgsqlConnection conn, string stream)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version FROM eventstore_streams WHERE streamname = :streamname FOR UPDATE";
                cmd.Parameters.AddWithValue("streamname", stream);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return reader.GetInt32(0);
                    else
                        return -1;
                }
            }
        }

        private static bool CreateStream(NpgsqlConnection conn, NpgsqlTransaction tran, string stream)
        {
            tran.Save("createstream");
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO eventstore_streams (streamname, version) VALUES (:streamname, 0)";
                    cmd.Parameters.AddWithValue("streamname", stream);
                    cmd.ExecuteNonQuery();
                    return false;
                }
            }
            catch (NpgsqlException ex)
            {
                if (ex.Code == "23505")
                {
                    tran.Rollback("createstream");
                    return true;
                }
                else
                    throw;
            }
        }

        private static void InsertNewEvents(NpgsqlConnection conn, string stream, int initialVersion, IEnumerable<EventStoreEvent> events)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO eventstore_events (streamname, version, format, eventtype, contents) " +
                    "VALUES (:streamname, :version, :format, :eventtype, :contents) RETURNING id";
                var paramStream = cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar);
                var paramVersion = cmd.Parameters.Add("version", NpgsqlDbType.Integer);
                var paramFormat = cmd.Parameters.Add("format", NpgsqlDbType.Varchar);
                var paramType = cmd.Parameters.Add("eventtype", NpgsqlDbType.Varchar);
                var paramBody = cmd.Parameters.Add("contents", NpgsqlDbType.Text);
                foreach (var evnt in events)
                {
                    evnt.StreamName = stream;
                    evnt.StreamVersion = ++initialVersion;
                    paramStream.Value = evnt.StreamName;
                    paramVersion.Value = evnt.StreamVersion;
                    paramFormat.Value = evnt.Format;
                    paramType.Value = evnt.Type;
                    paramBody.Value = evnt.Body;
                    var id = (long)cmd.ExecuteScalar();
                    evnt.Token = TokenFromId(id);
                }
            }
        }

        private static void UpdateStreamVersion(NpgsqlConnection conn, string stream, int version)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE eventstore_streams SET version = :version WHERE streamname = :streamname";
                cmd.Parameters.AddWithValue("streamname", stream);
                cmd.Parameters.AddWithValue("version", version);
                cmd.ExecuteNonQuery();
            }
        }

        private static void NotifyChanges(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "NOTIFY eventstore";
                cmd.ExecuteNonQuery();
            }
        }

        public Task<IEventStoreStream> ReadStream(string stream, int minVersion, int maxCount, bool loadBody)
        {
            return _db.Query(ReadStreamWorker, new ReadStreamParameters(stream, minVersion, maxCount, loadBody));
        }

        private class ReadStreamParameters
        {
            public readonly string Stream;
            public readonly int MinVersion;
            public readonly int MaxVersion;
            public readonly int MaxCount;
            public readonly bool LoadBody;

            public ReadStreamParameters(string stream, int minVersion, int maxCount, bool loadBody)
            {
                Stream = stream;
                MinVersion = Math.Max(1, minVersion);
                MaxCount = Math.Max(0, maxCount);
                LoadBody = loadBody;
                MaxVersion = MinVersion - 1 + MaxCount;
                if (MaxVersion < MinVersion)
                    MaxVersion = int.MaxValue;
            }
        }

        private IEventStoreStream ReadStreamWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (ReadStreamParameters)objContext;
            var version = GetStreamVersion(conn, context.Stream);
            if (version < context.MinVersion)
            {
                return new EventStoreStream(EmptyList, version, version);
            }
            else if (context.MaxCount <= 0)
            {
                return new EventStoreStream(EmptyList, version, context.MinVersion);
            }
            else
            {
                var events = LoadEvents(conn, context.Stream, context.MinVersion, Math.Min(version, context.MaxVersion));
                return new EventStoreStream(events, version, context.MinVersion);
            }
        }

        private static List<EventStoreEvent> LoadEvents(NpgsqlConnection conn, string stream, int minVersion, int maxVersion)
        {
            var expectedCount = maxVersion - minVersion + 1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, streamname, version, format, eventtype, contents FROM eventstore_events " +
                    "WHERE streamname = :streamname AND version >= :minversion AND version <= :maxversion " +
                    "ORDER BY version";
                cmd.Parameters.AddWithValue("streamname", stream);
                cmd.Parameters.AddWithValue("minversion", minVersion);
                cmd.Parameters.AddWithValue("maxversion", maxVersion);
                using (var reader = cmd.ExecuteReader())
                {
                    var list = new List<EventStoreEvent>(expectedCount);
                    while (reader.Read())
                    {
                        var evnt = new EventStoreEvent();
                        evnt.Token = TokenFromId(reader.GetInt64(0));
                        evnt.StreamName = reader.GetString(1);
                        evnt.StreamVersion = reader.GetInt32(2);
                        evnt.Format = reader.IsDBNull(3) ? null : reader.GetString(3);
                        evnt.Type = reader.IsDBNull(4) ? null : reader.GetString(4);
                        evnt.Body = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        list.Add(evnt);
                    }
                    return list;
                }
            }
        }

        public Task LoadBodies(IList<EventStoreEvent> events)
        {
            var ids = new Dictionary<long, EventStoreEvent>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                var evnt = events[i];
                if (evnt.Body != null)
                    continue;
                else
                {
                    var id = IdFromToken(evnt.Token);
                    ids[id] = evnt;
                }
            }
            if (ids == null || ids.Count == 0)
                return TaskUtils.CompletedTask();
            else
                return _db.Execute(LoadBodiesWorker, ids);
        }

        private void LoadBodiesWorker(NpgsqlConnection conn, object objContext)
        {
            var ids = (Dictionary<long, EventStoreEvent>)objContext;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, contents FROM eventstore_events WHERE id = ANY (:id)";
                var paramId = cmd.Parameters.Add("id", NpgsqlDbType.Bigint | NpgsqlDbType.Array);
                paramId.Value = ids;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        var body = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        EventStoreEvent evnt;
                        if (ids.TryGetValue(id, out evnt))
                            evnt.Body = body;
                    }
                }
            }
        }

        public Task<EventStoreSnapshot> LoadSnapshot(string stream)
        {
            return _db.Query<EventStoreSnapshot>(LoadSnapshotWorker, stream);
        }

        private EventStoreSnapshot LoadSnapshotWorker(NpgsqlConnection conn, object objContext)
        {
            var stream = (string)objContext;
            EventStoreSnapshot snapshot = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT format, snapshottype, contents FROM eventstore_snapshots WHERE streamname = :streamname";
                cmd.Parameters.AddWithValue("streamname", stream);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        snapshot = new EventStoreSnapshot();
                        snapshot.StreamName = stream;
                        snapshot.Format = reader.GetString(0);
                        snapshot.Type = reader.GetString(1);
                        snapshot.Body = reader.GetString(2);
                    }
                }
            }
            return snapshot;
        }

        public Task SaveSnapshot(string stream, EventStoreSnapshot snapshot)
        {
            return _db.Execute(SaveSnapshotWorker, new SaveSnapshotParameters(stream, snapshot));
        }

        private class SaveSnapshotParameters
        {
            public readonly string Stream;
            public readonly EventStoreSnapshot Snapshot;

            public SaveSnapshotParameters(string stream, EventStoreSnapshot snapshot)
            {
                Stream = stream;
                Snapshot = snapshot;
            }
        }

        private void SaveSnapshotWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (SaveSnapshotParameters)objContext;
            context.Snapshot.StreamName = context.Stream;

            using (var tran = conn.BeginTransaction())
            {
                var snapshotExists = FindSnapshot(conn, context.Snapshot);
                if (snapshotExists)
                {
                    UpdateSnapshot(conn, context.Snapshot);
                }
                else
                {
                    tran.Save("insertsnapshot");
                    try
                    {
                        TryInsertSnapshot(conn, context.Snapshot);
                    }
                    catch (NpgsqlException ex)
                    {
                        if (ex.Code == "23505")
                        {
                            tran.Rollback("insertsnapshot");
                            UpdateSnapshot(conn, context.Snapshot);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                tran.Commit();
            }
        }

        private static bool FindSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            var snapshotExists = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM eventstore_snapshots WHERE streamname = :streamname FOR UPDATE";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                using (var reader = cmd.ExecuteReader())
                    snapshotExists = reader.Read();
            }
            return snapshotExists;
        }

        private static void TryInsertSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO eventstore_snapshots (streamname, format, snapshottype, contents) VALUES (:streamname, :format, :snapshottype, :contents)";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                cmd.Parameters.Add("format", NpgsqlDbType.Varchar).Value = snapshot.Format;
                cmd.Parameters.Add("snapshottype", NpgsqlDbType.Varchar).Value = snapshot.Type;
                cmd.Parameters.Add("contents", NpgsqlDbType.Varchar).Value = snapshot.Body;
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpdateSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE eventstore_snapshots SET format = :format, snapshottype = :snapshottype, contents = :contents WHERE streamname = :streamname";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                cmd.Parameters.Add("format", NpgsqlDbType.Varchar).Value = snapshot.Format;
                cmd.Parameters.Add("snapshottype", NpgsqlDbType.Varchar).Value = snapshot.Type;
                cmd.Parameters.Add("contents", NpgsqlDbType.Varchar).Value = snapshot.Body;
                cmd.ExecuteNonQuery();
            }
        }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody)
        {
            return _db.Query(GetAllEventsWorker, new WaitForEventsContext(IdFromToken(token), maxCount, CancellationToken.None, true))
                .ContinueWith<IEventStoreCollection>(GetAllEventsConvert);
        }

        private GetAllEventsResponse GetAllEventsWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (WaitForEventsContext)objContext;
            var eventId = GetLastEventId(conn);
            if (context.EventId == -1)
                return new GetAllEventsResponse(null, eventId);
            var count = (int)Math.Min(context.MaxCount, eventId - context.EventId);
            if (count == 0)
                return new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
            var events = GetEvents(conn, context.EventId, count);
            if (events.Count == 0)
                return new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
            else
                return new GetAllEventsResponse(events, events[events.Count - 1].Token);
        }

        private IEventStoreCollection GetAllEventsConvert(Task<GetAllEventsResponse> task)
        {
            return task.Result.BuildFinal();
        }

        private class WaitForEventsContext
        {
            public int WaiterId;
            public long EventId;
            public int MaxCount;
            public CancellationToken Cancel;
            public TaskCompletionSource<IEventStoreCollection> Task;
            public CancellationTokenRegistration CancelRegistration;
            public bool Nowait;

            public WaitForEventsContext(long eventId, int maxCount, CancellationToken cancel, bool nowait)
            {
                EventId = eventId;
                MaxCount = maxCount;
                Cancel = cancel;
                Nowait = nowait || EventId != -1 && MaxCount > 0;
                if (Nowait)
                {
                    Task = new TaskCompletionSource<IEventStoreCollection>();
                }
            }
        }

        private class GetAllEventsEvent : EventStoreEvent
        {
            public long EventId { get; set; }
        }

        private class GetAllEventsResponse
        {
            private static IList<EventStoreEvent> NoEvents = new EventStoreEvent[0];
            public readonly IList<GetAllEventsEvent> Events;
            public readonly long EventId;
            public readonly EventStoreToken Token;

            public GetAllEventsResponse(IList<GetAllEventsEvent> events, long eventId)
            {
                Events = events;
                EventId = eventId;
            }
            public GetAllEventsResponse(IList<GetAllEventsEvent> events, EventStoreToken token)
            {
                Events = events;
                Token = token;
            }
            public EventStoreCollection BuildFinal()
            {
                var events = Events != null ? Events.Cast<EventStoreEvent>().ToList() : NoEvents;
                return new EventStoreCollection(events, Token ?? TokenFromId(EventId));
            }
        }

        public Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
        {
            StartNotifications();
            var waiter = new WaitForEventsContext(IdFromToken(token), maxCount, cancel, false);
            _db.Query(GetAllEventsWorker, waiter).ContinueWith(WaitForEvents_ImmediateFinished, waiter);
            return waiter.Task.Task;
        }

        private void WaitForEvents_ImmediateFinished(Task<GetAllEventsResponse> taskImmediate, object objContext)
        {
            var waiter = (WaitForEventsContext)objContext;
            if (taskImmediate.Exception != null)
            {
                waiter.Task.TrySetException(taskImmediate.Exception.InnerExceptions);
            }
            else if (taskImmediate.Result.Events.Count > 0 || waiter.Nowait)
            {
                waiter.Task.TrySetResult(taskImmediate.Result.BuildFinal());
            }
            else
            {
                if (waiter.Cancel.CanBeCanceled)
                {
                    waiter.CancelRegistration = waiter.Cancel.Register(WaitForEvents_RemoveWaiter, waiter);
                }
                while (true)
                {
                    waiter.WaiterId = Interlocked.Increment(ref _waiterId);
                    if (_waiters.TryAdd(waiter.WaiterId, waiter))
                    {
                        if (waiter.Cancel.IsCancellationRequested)
                            WaitForEvents_RemoveWaiter(waiter);
                        else
                            _notified.Set();
                    }
                }
            }
        }

        private void WaitForEvents_RemoveWaiter(object param)
        {
            WaitForEventsContext waiter = (WaitForEventsContext)param;
            WaitForEventsContext removedWaiter;
            _waiters.TryRemove(waiter.WaiterId, out removedWaiter);
            waiter.Task.TrySetCanceled();
        }

        private void WaitForEvents_Timer(Task task)
        {
            if (task.Exception == null && !task.IsCanceled)
            {
                _notified.Set();
                _time.Delay(5000, _cancelListening.Token).ContinueWith(WaitForEvents_Timer);
            }
        }

        private IEnumerable<Task> WaitForEvents_CoreInternal()
        {
            var lastKnownEventId = 0L;
            _time.Delay(5000, _cancelListening.Token).ContinueWith(WaitForEvents_Timer);
            while (!_disposed)
            {
                var taskWait = _notified.Wait();
                yield return taskWait;
                taskWait.Wait();
                var minEventId = long.MaxValue;
                var maxCount = 0;
                foreach (var waiter in _waiters.Values)
                {
                    if (minEventId > waiter.EventId)
                        minEventId = waiter.EventId;
                    if (maxCount < waiter.MaxCount)
                        maxCount = waiter.MaxCount;
                }
                if (maxCount == 0)
                    continue;
                var taskQuery = _db.Query(GetAllEventsWorker, new WaitForEventsContext(Math.Max(lastKnownEventId, minEventId), maxCount, CancellationToken.None, true));
                yield return taskQuery;
                try
                {
                    taskQuery.Wait();
                }
                catch
                {
                    continue;
                }
                var results = taskQuery.Result;
                if (results.Events.Count == 0)
                    continue;
                foreach (var waiter in _waiters.Values)
                {
                    WaitForEventsContext removedWaiter;
                    var eventsForWaiter = results.Events.Where(e => e.EventId > waiter.EventId).Take(waiter.MaxCount).Cast<EventStoreEvent>().ToList();
                    if (eventsForWaiter.Count == 0)
                        continue;
                    if (_waiters.TryRemove(waiter.WaiterId, out removedWaiter))
                    {
                        var result = new EventStoreCollection(eventsForWaiter, eventsForWaiter[eventsForWaiter.Count - 1].Token);
                        waiter.Task.TrySetResult(result);
                        waiter.CancelRegistration.Dispose();
                    }
                }
            }
            foreach (var waiter in _waiters.Values)
            {
                waiter.CancelRegistration.Dispose();
                waiter.Task.TrySetCanceled();
            }
        }

        private static long GetLastEventId(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM eventstore_events ORDER BY id DESC LIMIT 1";
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return reader.GetInt64(0);
                    else
                        return 0;
                }
            }
        }

        private static List<GetAllEventsEvent> GetEvents(NpgsqlConnection conn, long startingId, int maxCount)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT id, streamname, version, format, eventtype, contents FROM eventstore_events WHERE id > ",
                    startingId.ToString(), " ORDER BY id LIMIT ", maxCount.ToString());
                using (var reader = cmd.ExecuteReader())
                {
                    var list = new List<GetAllEventsEvent>(maxCount);
                    while (reader.Read())
                    {
                        var evnt = new GetAllEventsEvent();
                        evnt.EventId = reader.GetInt64(0);
                        evnt.Token = TokenFromId(evnt.EventId);
                        evnt.StreamName = reader.GetString(1);
                        evnt.StreamVersion = reader.GetInt32(2);
                        evnt.Format = reader.GetString(3);
                        evnt.Type = reader.GetString(4);
                        evnt.Body = reader.GetString(5);
                        list.Add(evnt);
                    }
                    return list;
                }
            }
        }

        private static EventStoreToken TokenFromId(long id)
        {
            if (id == 0)
                return EventStoreToken.Initial;
            else
                return new EventStoreToken(id.ToString());
        }
        private static long IdFromToken(EventStoreToken token)
        {
            if (token.IsInitial)
                return 0;
            else if (token.IsCurrent)
                return -1;
            else
                return long.Parse(token.ToString());
        }
    }
}
