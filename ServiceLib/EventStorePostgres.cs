using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace ServiceLib
{
    public class EventStorePostgres : IEventStoreWaitable, IDisposable
    {
        private readonly DatabasePostgres _db;
        private readonly ITime _time;
        private static readonly List<EventStoreEvent> EmptyList = new List<EventStoreEvent>();
        private int _waiterId;
        private readonly ConcurrentDictionary<int, WaitForEventsContext> _waiters;
        private readonly AutoResetEventAsync _notified;
        private bool _disposed;
        private int _canStartNotifications;
        private readonly CancellationTokenSource _cancelListening;
        private Task _timerTask, _notificationTask;
        private readonly string _partition;
        private static readonly EventStorePostgresTraceSource Logger = new EventStorePostgresTraceSource("ServiceLib.EventStore");

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
            WaitForTaskFinish(_timerTask);
            WaitForTaskFinish(_notificationTask);
            _cancelListening.Dispose();
        }

        private void WaitForTaskFinish(Task task)
        {
            try
            {
                if (task == null)
                    return;
                task.Wait(1000);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException)
            {
            }
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
                    {
                        Logger.AddToStreamConflicts(context.Stream, realVersion, context.ExpectedVersion);
                        return false;
                    }
                    if (rawVersion == -1)
                    {
                        if (CreateStream(conn, tran, context.Stream))
                        {
                            rawVersion = GetStreamVersion(conn, context.Stream);
                            realVersion = rawVersion == -1 ? 0 : rawVersion;
                            if (!context.ExpectedVersion.VerifyVersion(realVersion))
                            {
                                Logger.AddToStreamConflicts(context.Stream, realVersion, context.ExpectedVersion);
                                return false;
                            }
                        }
                    }
                    var events = context.Events.ToList();
                    InsertNewEvents(conn, context.Stream, realVersion, events);
                    UpdateStreamVersion(conn, context.Stream, realVersion + events.Count);
                    NotifyChanges(conn);
                    tran.Commit();
                    Logger.AddToStreamComplete(context.Stream, events);
                    return true;
                }
            }
        }

        private int GetStreamVersion(NpgsqlConnection conn, string stream)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat("SELECT version FROM ", _partition, "_streams WHERE streamname = :streamname FOR UPDATE");
                cmd.Parameters.AddWithValue("streamname", stream);
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    int version;
                    if (reader.Read())
                        version = reader.GetInt32(0);
                    else
                        version = -1;
                    Logger.GotStreamVersion(stream, version);
                    return version;
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
                    cmd.CommandText = string.Concat(
                        "INSERT INTO ", _partition, "_streams (streamname, version) VALUES (:streamname, 0)");
                    cmd.Parameters.AddWithValue("streamname", stream);
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                    Logger.StreamCreated(stream);
                    return false;
                }
            }
            catch (NpgsqlException ex)
            {
                if (ex.Code == "23505")
                {
                    tran.Rollback("createstream");
                    Logger.StreamCreationConflicted(stream);
                    return true;
                }
                else
                {
                    Logger.StreamCreationFailed(stream, ex);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.StreamCreationFailed(stream, ex);
                throw;
            }
        }

        private void InsertNewEvents(NpgsqlConnection conn, string stream, int initialVersion, IEnumerable<EventStoreEvent> events)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "INSERT INTO ", _partition, "_events (streamname, version, format, eventtype, contents) ",
                    "VALUES (:streamname, :version, :format, :eventtype, :contents) RETURNING id");
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
                    Logger.InsertedEvent(evnt.StreamName, evnt.StreamVersion, evnt.Token);
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
            return _db.Query(ReadStreamWorker, new ReadStreamParameters(stream, minVersion, maxCount));
        }

        private class ReadStreamParameters
        {
            public readonly string Stream;
            public readonly int MinVersion;
            public readonly int MaxVersion;
            public readonly int MaxCount;

            public ReadStreamParameters(string stream, int minVersion, int maxCount)
            {
                Stream = stream;
                MinVersion = Math.Max(1, minVersion);
                MaxCount = Math.Max(0, maxCount);
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
                    Logger.ReadFromStreamComplete(context.Stream, context.MinVersion, context.MaxCount, version, EmptyList);
                    return new EventStoreStream(EmptyList, version, version);
                }
                else if (context.MaxCount <= 0)
                {
                    Logger.ReadFromStreamComplete(context.Stream, context.MinVersion, context.MaxCount, version, EmptyList);
                    return new EventStoreStream(EmptyList, version, context.MinVersion);
                }
                else
                {
                    var events = LoadEvents(conn, context.Stream, context.MinVersion, Math.Min(version, context.MaxVersion));
                    Logger.ReadFromStreamComplete(context.Stream, context.MinVersion, context.MaxCount, version, events);
                    return new EventStoreStream(events, version, context.MinVersion);
                }
            }
        }

        private List<EventStoreEvent> LoadEvents(NpgsqlConnection conn, string stream, int minVersion, int maxVersion)
        {
            var expectedCount = maxVersion - minVersion + 1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT id, streamname, version, format, eventtype, contents FROM ", _partition, "_events ",
                    "WHERE streamname = :streamname AND version >= :minversion AND version <= :maxversion ",
                    "ORDER BY version");
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
            foreach (var evnt in events)
            {
                if (evnt.Body != null) 
                    continue;
                var id = IdFromToken(evnt.Token);
                ids[id] = evnt;
            }
            if (ids.Count == 0)
                return TaskUtils.CompletedTask();
            else
                return _db.Execute(LoadBodiesWorker, ids);
        }

        [SuppressMessage("ReSharper", "BitwiseOperatorOnEnumWithoutFlags")]
        private void LoadBodiesWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "LoadBodies"))
            {
                var ids = (Dictionary<long, EventStoreEvent>)objContext;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat("SELECT id, contents FROM ", _partition, "_events WHERE id = ANY (:id)");
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
                Logger.LoadBodiesFinished(ids.Count);
            }
        }

        public Task<EventStoreSnapshot> LoadSnapshot(string stream)
        {
            return _db.Query(LoadSnapshotWorker, stream);
        }

        private EventStoreSnapshot LoadSnapshotWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "LoadSnapshot"))
            {
                var stream = (string)objContext;
                EventStoreSnapshot snapshot = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "SELECT format, snapshottype, contents FROM ", _partition, "_snapshots WHERE streamname = :streamname");
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
                Logger.LoadSnapshotFinished(stream, snapshot);
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

                try
                {
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
                                    Logger.SnapshotInsertConflicted(context.Snapshot);
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
                    Logger.SaveSnapshotFinished(context.Stream, context.Snapshot);
                }
                catch (Exception exception)
                {
                    Logger.SaveSnapshotFailed(context.Stream, context.Snapshot, exception);
                    throw;
                }
            }
        }

        private bool FindSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            bool snapshotExists;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT 1 FROM ", _partition, "_snapshots WHERE streamname = :streamname FOR UPDATE");
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                    snapshotExists = reader.Read();
            }
            Logger.SnapshotFound(snapshot.StreamName, snapshotExists);
            return snapshotExists;
        }

        private void TryInsertSnapshot(NpgsqlConnection conn, EventStoreSnapshot snapshot)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "INSERT INTO ", _partition,
                    "_snapshots (streamname, format, snapshottype, contents) VALUES (:streamname, :format, :snapshottype, :contents)");
                cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar).Value = snapshot.StreamName;
                cmd.Parameters.Add("format", NpgsqlDbType.Varchar).Value = snapshot.Format;
                cmd.Parameters.Add("snapshottype", NpgsqlDbType.Varchar).Value = snapshot.Type;
                cmd.Parameters.Add("contents", NpgsqlDbType.Varchar).Value = snapshot.Body;
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
                Logger.SnapshotInserted(snapshot);
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
                Logger.SnapshotUpdated(snapshot);
            }
        }

        public async Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody)
        {
            var context = new WaitForEventsContext(token, maxCount, CancellationToken.None, true);
            var immediateResult = await _db.Query(GetAllEventsWorker, context);
            var response = immediateResult.BuildFinal();
            Logger.GetAllEventsComplete(context.Token, context.EventId, context.MaxCount, response.Events, response.NextToken);
            return response;
        }

        private GetAllEventsResponse GetAllEventsWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "GetAllEventsWorker"))
            {
                var context = (WaitForEventsContext)objContext;
                var eventId = GetLastEventId(conn);
                if (context.EventId == -1)
                {
                    var response = new GetAllEventsResponse(null, eventId);
                    LogGetAllEventsChecked(context, response);
                    return response;
                }
                var count = (int)Math.Min(context.MaxCount, eventId - context.EventId);
                if (count == 0)
                {
                    var response = new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
                    LogGetAllEventsChecked(context, response);
                    return response;
                }
                var events = GetEvents(conn, context.EventId, count);
                if (events.Count == 0)
                {
                    var response = new GetAllEventsResponse(null, Math.Min(context.EventId, eventId));
                    LogGetAllEventsChecked(context, response);
                    return response;
                }
                else
                {
                    var response = new GetAllEventsResponse(events, events[events.Count - 1].Token);
                    LogGetAllEventsChecked(context, response);
                    return response;
                }
            }
        }

        private void LogGetAllEventsChecked(WaitForEventsContext context, GetAllEventsResponse response)
        {
            var taskId = context.Nowait ? 0 : context.Task.Task.Id;
            Logger.WaitForEventsCheckedForData(context.Token, context.EventId, context.MaxCount, response.FinalEvents, response.NextToken, taskId);
        }

        private class WaitForEventsContext
        {
            public int WaiterId;
            public readonly EventStoreToken Token;
            public readonly long EventId;
            public readonly int MaxCount;
            public CancellationToken Cancel;
            public readonly TaskCompletionSource<GetAllEventsResponse> Task;
            public CancellationTokenRegistration CancelRegistration;
            public readonly bool Nowait;

            public WaitForEventsContext(EventStoreToken token, int maxCount, CancellationToken cancel, bool nowait)
            {
                Token = token;
                EventId = IdFromToken(token);
                MaxCount = maxCount;
                Cancel = cancel;
                Nowait = nowait || EventId == -1 || MaxCount == 0;
                if (!nowait)
                {
                    Task = new TaskCompletionSource<GetAllEventsResponse>();
                    if (Cancel.IsCancellationRequested)
                    {
                        Task.TrySetCanceled();
                    }
                }
            }
        }

        private class GetAllEventsEvent : EventStoreEvent
        {
            public long EventId { get; set; }
        }

        private class GetAllEventsResponse
        {
            private static readonly IList<EventStoreEvent> NoEvents = new EventStoreEvent[0];
            private readonly IList<GetAllEventsEvent> _events;
            private readonly long _eventId;
            private readonly EventStoreToken _token;

            public GetAllEventsResponse(IList<GetAllEventsEvent> events, long eventId)
            {
                _events = events;
                _eventId = eventId;
            }
            public GetAllEventsResponse(IList<GetAllEventsEvent> events, EventStoreToken token)
            {
                _events = events;
                _token = token;
            }

            public EventStoreCollection BuildFinal()
            {
                return new EventStoreCollection(FinalEvents, NextToken);
            }

            public EventStoreToken NextToken
            {
                get { return _token ?? TokenFromId(_eventId); }
            }

            public IList<GetAllEventsEvent> Events
            {
                get { return _events; }
            } 

            public IList<EventStoreEvent> FinalEvents
            {
                get { return _events != null ? _events.Cast<EventStoreEvent>().ToList() : NoEvents; }
            }

            public bool HasAnyEvents
            {
                get { return _events != null && _events.Count > 0; }
            }
        }

        public async Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
        {
            StartNotifications();
            var context = new WaitForEventsContext(token, maxCount, cancel, false);
            try
            {
                var immediateResult = await _db.Query(GetAllEventsWorker, context);

                if (!context.Nowait && !immediateResult.HasAnyEvents)
                {
                    if (context.Cancel.CanBeCanceled)
                    {
                        context.CancelRegistration = context.Cancel.Register(WaitForEvents_Cancelled, context);
                    }
                    Logger.WaitForEventsInitialized(
                        context.Token, context.EventId, context.MaxCount, context.Task.Task.Id);
                    while (true)
                    {
                        context.WaiterId = Interlocked.Increment(ref _waiterId);
                        if (_waiters.TryAdd(context.WaiterId, context))
                        {
                            _notified.Set();
                            break;
                        }
                    }

                    immediateResult = await context.Task.Task;
                }

                var finalResponse = immediateResult.BuildFinal();
                Logger.WaitForEventsComplete(
                    context.Token, context.EventId, context.MaxCount, finalResponse.Events, finalResponse.NextToken);
                return finalResponse;
            }
            catch (OperationCanceledException)
            {
                Logger.WaitForEventsCancelled(context.Token, context.Task.Task.Id);
                throw;
            }
            catch (Exception exception)
            {
                Logger.WaitForEventsFailed(context.Token, context.EventId, context.MaxCount, exception);
                throw;
            }
        }

        private void StartNotifications()
        {
            if (Interlocked.Exchange(ref _canStartNotifications, 0) == 1)
            {
                _db.Listen(_partition, s => _notified.Set(), _cancelListening.Token);
                _timerTask = WaitForEvents_Timer();
                _notificationTask = WaitForEvents_CheckingLoop();
            }
        }

        private void WaitForEvents_Cancelled(object param)
        {
            var waiter = (WaitForEventsContext)param;
            WaitForEventsContext removedWaiter;
            _waiters.TryRemove(waiter.WaiterId, out removedWaiter);
            waiter.Task.TrySetCanceled();
        }

        private async Task WaitForEvents_Timer()
        {
            var cancelToken = _cancelListening.Token;
            try
            {
                while (!_disposed)
                {
                    cancelToken.ThrowIfCancellationRequested();
                    await _time.Delay(5000, cancelToken);
                    _notified.Set();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.WaitForEventsTimerCrashed(exception);
            }
        }
    

        private async Task WaitForEvents_CheckingLoop()
        {
            try
            {
                while (!_disposed)
                {
                    await _notified.Wait();
                    var minEventId = long.MaxValue;
                    var maxCount = 0;
                    foreach (var waiter in _waiters.Values)
                    {
                        if (minEventId > waiter.EventId)
                            minEventId = waiter.EventId;
                        if (maxCount < waiter.MaxCount)
                            maxCount = waiter.MaxCount;
                    }
                    if (maxCount != 0)
                    {
                        try
                        {
                            var immediateContext = new WaitForEventsContext(
                                TokenFromId(minEventId), maxCount, CancellationToken.None, false);
                            var results = await _db.Query(GetAllEventsWorker, immediateContext);
                            SendNewEventsToWaiters(results);
                        }
                        catch (Exception exception)
                        {
                            Logger.WaitForEventsFailedToGetNewData(exception);
                        }
                    }
                    RemoveCancelledWaiters();
                }
            }
            finally
            {
                CleanupWaiters();
            }
        }

        private void SendNewEventsToWaiters(GetAllEventsResponse results)
        {
            if (results.Events == null || results.Events.Count == 0) 
                return;
            foreach (var waiter in _waiters.Values)
            {
                WaitForEventsContext removedWaiter;
                var eventsForWaiter = FilterEventsForWaiter(results, waiter);
                if (eventsForWaiter.Count == 0) 
                    continue;
                if (_waiters.TryRemove(waiter.WaiterId, out removedWaiter))
                {
                    var nextToken = eventsForWaiter[eventsForWaiter.Count - 1].Token;
                    waiter.Task.TrySetResult(new GetAllEventsResponse(eventsForWaiter, nextToken));
                    waiter.CancelRegistration.Dispose();
                }
            }
        }

        private static List<GetAllEventsEvent> FilterEventsForWaiter(GetAllEventsResponse results, WaitForEventsContext waiter)
        {
            return results.Events.Where(e => e.EventId > waiter.EventId).Take(waiter.MaxCount).ToList();
        }

        private void RemoveCancelledWaiters()
        {
            foreach (var waiter in _waiters.Values)
            {
                if (waiter.Cancel.IsCancellationRequested)
                {
                    WaitForEventsContext removedWaiter;
                    _waiters.TryRemove(waiter.WaiterId, out removedWaiter);
                    waiter.Task.TrySetCanceled();
                }
            }
        }

        private void CleanupWaiters()
        {
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
