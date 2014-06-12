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
using log4net;

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
        private string _partition;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.EventStore");

        public EventStorePostgres(DatabasePostgres db, ITime time, string partition = "eventstore")
        {
            _db = db;
            _time = time;
            _waiters = new ConcurrentDictionary<int, WaitForEventsContext>();
            _notified = new AutoResetEventAsync();
            _canStartNotifications = 1;
            _cancelListening = new CancellationTokenSource();
            _partition = partition;
        }

        private void StartNotifications()
        {
            if (Interlocked.Exchange(ref _canStartNotifications, 0) == 1)
            {
                _db.Listen(_partition, s => _notified.Set(), _cancelListening.Token);
                _notificationTask = TaskUtils.FromEnumerable(WaitForEvents_CoreInternal()).CatchAll().GetTask();
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
            using (new LogMethod(Logger, "InitializeDatabase"))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + _partition + "_streams (" +
                        "streamname varchar PRIMARY KEY, " +
                        "version integer NOT NULL" +
                        ")";
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + _partition + "_events (" +
                        "id bigserial PRIMARY KEY, " +
                        "streamname varchar NOT NULL, " +
                        "version integer NOT NULL, " +
                        "format varchar, eventtype varchar, contents text, " +
                        "UNIQUE (streamname, version)" +
                        ")";
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + _partition + "_snapshots (" +
                        "streamname varchar PRIMARY KEY, " +
                        "format varchar, snapshottype varchar, contents text" +
                        ")";
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                }
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
            using (new LogMethod(Logger, "AddToStream"))
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
                    Logger.DebugFormat("AddToStream({0}, {1} events): saved", context.Stream, events.Count);
                    return true;
                }
            }
        }

        private int GetStreamVersion(NpgsqlConnection conn, string stream)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version FROM " + _partition + "_streams WHERE streamname = :streamname FOR UPDATE";
                cmd.Parameters.AddWithValue("streamname", stream);
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return reader.GetInt32(0);
                    else
                        return -1;
                }
            }
        }

        private bool CreateStream(NpgsqlConnection conn, NpgsqlTransaction tran, string stream)
        {
            tran.Save("createstream");
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO " + _partition + "_streams (streamname, version) VALUES (:streamname, 0)";
                    cmd.Parameters.AddWithValue("streamname", stream);
                    Logger.TraceSql(cmd);
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

        private void InsertNewEvents(NpgsqlConnection conn, string stream, int initialVersion, IEnumerable<EventStoreEvent> events)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO " + _partition + "_events (streamname, version, format, eventtype, contents) " +
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
                    Logger.TraceSql(cmd);
                    var id = (long)cmd.ExecuteScalar();
                    evnt.Token = TokenFromId(id);
                }
            }
        }

        private void UpdateStreamVersion(NpgsqlConnection conn, string stream, int version)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE " + _partition + "_streams SET version = :version WHERE streamname = :streamname";
                cmd.Parameters.AddWithValue("streamname", stream);
                cmd.Parameters.AddWithValue("version", version);
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private void NotifyChanges(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "NOTIFY " + _partition;
                Logger.TraceSql(cmd);
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
            using (new LogMethod(Logger, "ReadStream"))
            {
                var context = (ReadStreamParameters)objContext;
                var version = Math.Max(0, GetStreamVersion(conn, context.Stream));
                if (version < context.MinVersion)
                {
                    Logger.DebugFormat("ReadStream({0}, {1}, {2}): returning {3} events",
                        context.Stream, context.MinVersion, context.MaxCount, 0);
                    return new EventStoreStream(EmptyList, version, version);
                }
                else if (context.MaxCount <= 0)
                {
                    Logger.DebugFormat("ReadStream({0}, {1}, {2}): returning {3} events",
                        context.Stream, context.MinVersion, context.MaxCount, 0);
                    return new EventStoreStream(EmptyList, version, context.MinVersion);
                }
                else
                {
                    var events = LoadEvents(conn, context.Stream, context.MinVersion, Math.Min(version, context.MaxVersion));
                    Logger.DebugFormat("ReadStream({0}, {1}, {2}): returning {3} events",
                        context.Stream, context.MinVersion, context.MaxCount, events.Count);
                    return new EventStoreStream(events, version, context.MinVersion);
                }
            }
        }

        private List<EventStoreEvent> LoadEvents(NpgsqlConnection conn, string stream, int minVersion, int maxVersion)
        {
            var expectedCount = maxVersion - minVersion + 1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, streamname, version, format, eventtype, contents FROM " + _partition + "_events " +
                    "WHERE streamname = :streamname AND version >= :minversion AND version <= :maxversion " +
                    "ORDER BY version";
                cmd.Parameters.AddWithValue("streamname", stream);
                cmd.Parameters.AddWithValue("minversion", minVersion);
                cmd.Parameters.AddWithValue("maxversion", maxVersion);
                Logger.TraceSql(cmd);
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
            using (new LogMethod(Logger, "LoadBodies"))
            {
                var ids = (Dictionary<long, EventStoreEvent>)objContext;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, contents FROM " + _partition + "_events WHERE id = ANY (:id)";
                    var paramId = cmd.Parameters.Add("id", NpgsqlDbType.Bigint | NpgsqlDbType.Array);
                    paramId.Value = ids;
                    Logger.TraceSql(cmd);
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
                Logger.DebugFormat("LoadBodies({0} events)", ids.Count);
            }
        }

        public Task<EventStoreSnapshot> LoadSnapshot(string stream)
        {
            return _db.Query<EventStoreSnapshot>(LoadSnapshotWorker, stream);
        }

        private EventStoreSnapshot LoadSnapshotWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "LoadSnapshot"))
            {
                var stream = (string)objContext;
                EventStoreSnapshot snapshot = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT format, snapshottype, contents FROM " + _partition + "_snapshots WHERE streamname = :streamname";
                    cmd.Parameters.AddWithValue("streamname", stream);
                    Logger.TraceSql(cmd);
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
                Logger.DebugFormat("LoadSnapshot({0}): {1}found", stream, snapshot == null ? "not " : "");
                return snapshot;
            }
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
            using (new LogMethod(Logger, "SaveSnapshot"))
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
                Logger.DebugFormat("SaveSnapshot({0})", context.Stream);
            }
        }

        private bool FindSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            var snapshotExists = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM " + _partition + "_snapshots WHERE streamname = :streamname FOR UPDATE";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                    snapshotExists = reader.Read();
            }
            return snapshotExists;
        }

        private void TryInsertSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO " + _partition + "_snapshots (streamname, format, snapshottype, contents) VALUES (:streamname, :format, :snapshottype, :contents)";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                cmd.Parameters.Add("format", NpgsqlDbType.Varchar).Value = snapshot.Format;
                cmd.Parameters.Add("snapshottype", NpgsqlDbType.Varchar).Value = snapshot.Type;
                cmd.Parameters.Add("contents", NpgsqlDbType.Varchar).Value = snapshot.Body;
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private void UpdateSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE " + _partition + "_snapshots SET format = :format, snapshottype = :snapshottype, contents = :contents WHERE streamname = :streamname";
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                cmd.Parameters.Add("format", NpgsqlDbType.Varchar).Value = snapshot.Format;
                cmd.Parameters.Add("snapshottype", NpgsqlDbType.Varchar).Value = snapshot.Type;
                cmd.Parameters.Add("contents", NpgsqlDbType.Varchar).Value = snapshot.Body;
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody)
        {
            return _db.Query(GetAllEventsWorker, new WaitForEventsContext(null, IdFromToken(token), maxCount, CancellationToken.None, true))
                .ContinueWith<IEventStoreCollection>(GetAllEventsConvert);
        }

        private GetAllEventsResponse GetAllEventsWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "GetAllEvents"))
            {
                var context = (WaitForEventsContext)objContext;
                var eventId = GetLastEventId(conn);
                if (context.EventId == -1)
                {
                    Logger.DebugFormat("GetAllEvents(eventId: {0}): token was Current", context.EventId);
                    return new GetAllEventsResponse(null, eventId);
                }
                var count = (int)Math.Min(context.MaxCount, eventId - context.EventId);
                if (count == 0)
                {
                    Logger.DebugFormat("GetAllEvents(eventId: {0}): zero MaxCount", context.EventId);
                    return new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
                }
                var events = GetEvents(conn, context.EventId, count);
                if (events.Count == 0)
                {
                    Logger.DebugFormat("GetAllEvents(eventId: {0}): no events available", context.EventId);
                    return new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
                }
                else
                {
                    Logger.DebugFormat("GetAllEvents(eventId: {0}): returning {1} events", context.EventId, events.Count);
                    return new GetAllEventsResponse(events, events[events.Count - 1].Token);
                }
            }
        }

        private IEventStoreCollection GetAllEventsConvert(Task<GetAllEventsResponse> task)
        {
            return task.Result.BuildFinal();
        }

        private class WaitForEventsContext
        {
            public EventStorePostgres Parent;
            public int WaiterId;
            public long EventId;
            public int MaxCount;
            public CancellationToken Cancel;
            public TaskCompletionSource<IEventStoreCollection> Task;
            public CancellationTokenRegistration CancelRegistration;
            public bool Nowait;

            public WaitForEventsContext(EventStorePostgres parent, long eventId, int maxCount, CancellationToken cancel, bool nowait)
            {
                Parent = parent;
                EventId = eventId;
                MaxCount = maxCount;
                Cancel = cancel;
                Nowait = nowait || EventId == -1 || MaxCount == 0;
                if (!nowait)
                {
                    Task = new TaskCompletionSource<IEventStoreCollection>();
                }
            }

            public void ImmediateFinished(Task<GetAllEventsResponse> task)
            {
                Parent.WaitForEvents_ImmediateFinished(task, this);
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
            var waiter = new WaitForEventsContext(this, IdFromToken(token), maxCount, cancel, false);
            _db.Query(GetAllEventsWorker, waiter).ContinueWith(waiter.ImmediateFinished);
            return waiter.Task.Task;
        }

        private void WaitForEvents_ImmediateFinished(Task<GetAllEventsResponse> taskImmediate, object objContext)
        {
            var waiter = (WaitForEventsContext)objContext;
            if (taskImmediate.Exception != null)
            {
                Logger.DebugFormat("WaitForEvents(eventId: {0}): failed", waiter.EventId);
                waiter.Task.TrySetException(taskImmediate.Exception.InnerExceptions);
            }
            else if (waiter.Nowait)
            {
                var finalResult = taskImmediate.Result.BuildFinal();
                Logger.DebugFormat("WaitForEvents(eventId: {0}): returning {1} events", waiter.EventId, finalResult.Events.Count);
                waiter.Task.TrySetResult(finalResult);
            }
            else if (taskImmediate.Result.Events != null && taskImmediate.Result.Events.Count > 0)
            {
                var finalResult = taskImmediate.Result.BuildFinal();
                Logger.DebugFormat("WaitForEvents(eventId: {0}): returning {1} events", waiter.EventId, finalResult.Events.Count);
                waiter.Task.TrySetResult(finalResult);
            }
            else
            {
                if (waiter.Cancel.IsCancellationRequested)
                {
                    WaitForEvents_RemoveWaiter(waiter);
                    return;
                }
                else if (waiter.Cancel.CanBeCanceled)
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
                        break;
                    }
                }
            }
        }

        private void WaitForEvents_RemoveWaiter(object param)
        {
            WaitForEventsContext waiter = (WaitForEventsContext)param;
            WaitForEventsContext removedWaiter;
            _waiters.TryRemove(waiter.WaiterId, out removedWaiter);
            waiter.Task.TrySetResult(new EventStoreCollection(new EventStoreEvent[0], TokenFromId(waiter.EventId)));
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
                var taskQuery = _db.Query(GetAllEventsWorker, new WaitForEventsContext(null, Math.Max(lastKnownEventId, minEventId), maxCount, CancellationToken.None, true));
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
                if (results.Events == null || results.Events.Count == 0)
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

        private long GetLastEventId(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM " + _partition + "_events ORDER BY id DESC LIMIT 1";
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return reader.GetInt64(0);
                    else
                        return 0;
                }
            }
        }

        private List<GetAllEventsEvent> GetEvents(NpgsqlConnection conn, long startingId, int maxCount)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT id, streamname, version, format, eventtype, contents FROM " + _partition + "_events WHERE id > ",
                    startingId.ToString(), " ORDER BY id LIMIT ", maxCount.ToString());
                Logger.TraceSql(cmd);
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
