using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Threading;

namespace ServiceLib
{
    public class EventStorePostgres : IEventStoreWaitable, IDisposable
    {
        private DatabasePostgres _db;
        private static EventStoreEvent[] EmptyList = new EventStoreEvent[0];

        public EventStorePostgres(DatabasePostgres db)
        {
            _db = db;
        }

        public void Initialize()
        {
            _db.ExecuteSync(InitializeDatabase);
        }

        public void Dispose()
        {
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
            return _db.Query(GetAllEventsWorker, new GetAllEventsContext(token, maxCount, loadBody, true));
        }

        private class GetAllEventsContext
        {
            public readonly EventStoreToken Token;
            public readonly int MaxCount;
            public readonly bool LoadBody;
            public readonly bool Nowait;

            public GetAllEventsContext(EventStoreToken token, int maxCount, bool loadBody, bool nowait)
            {
                Token = token;
                MaxCount = maxCount;
                LoadBody = loadBody;
                Nowait = nowait;
            }

        }

        private IEventStoreCollection GetAllEventsWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (GetAllEventsContext)objContext;

        }

        public Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
        {
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
#if false
        private class GetAllEventsWorker : IDisposable
        {
            public long StartingId;
            public EventStoreToken PublicToken;
            public int MaxCount;
            public bool LoadBody;
            public Action<IEventStoreCollection> OnComplete;
            public Action<Exception> OnError;
            public bool Nowait;
            private EventStorePostgres _parent;
            private IDisposable _listening;
            private bool _busy;
            private bool _notified;

            public GetAllEventsWorker(EventStorePostgres parent, bool nowait, EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this.PublicToken = token;
                this.MaxCount = Math.Max(0, Math.Min(1000, maxCount));
                this.LoadBody = loadBody;
                this.OnComplete = onComplete;
                this.OnError = onError;
                this.Nowait = nowait;
                this.StartingId = IdFromToken(token);
            }

            public void Execute()
            {
                _parent._db.Execute(ExecuteDb, OnError);
            }

            private void ExecuteDb(NpgsqlConnection conn)
            {
                if (MaxCount == 0)
                {
                    var version = GetLastId(conn);
                    _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                        OnComplete, EmptyList, TokenFromId(version)));
                }
                else if (StartingId == -1)
                {
                    StartingId = GetLastId(conn);
                    if (Nowait)
                    {
                        _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                            OnComplete, EmptyList, TokenFromId(StartingId)));
                    }
                    else
                        _listening = _parent._changes.Register(OnNotified);
                }
                else
                {
                    var events = GetEvents(conn);
                    if (events.Count > 0)
                    {
                        _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                            OnComplete, events, events.Last().Token));
                    }
                    else if (Nowait)
                    {
                        var version = GetLastId(conn);
                        if (version == StartingId)
                        {
                            _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                                OnComplete, EmptyList, TokenFromId(version)));
                        }
                        else
                        {
                            events = GetEvents(conn);
                            if (events.Count > 0)
                            {
                                _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                                    OnComplete, events, events.Last().Token));
                            }
                            else
                            {
                                _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                                    OnComplete, EmptyList, TokenFromId(version)));
                            }
                        }
                    }
                    else
                        _listening = _parent._changes.Register(OnNotified);
                }
            }

            private void OnNotified()
            {
                lock (this)
                {
                    _notified = true;
                    if (_busy)
                        return;
                    _busy = true;
                    _notified = false;
                }
                _parent._db.Execute(OnNotifiedDb, OnError);
            }

            private void OnNotifiedDb(NpgsqlConnection conn)
            {
                bool repeat = true;
                while (repeat)
                {
                    repeat = false;
                    var events = GetEvents(conn);
                    if (events.Count > 0)
                    {
                        _listening.Dispose();
                        _parent._executor.Enqueue(new EventStoreGetAllEventsComplete(
                            OnComplete, events, events.Last().Token));
                        return;
                    }
                    else
                    {
                        lock (this)
                        {
                            if (_notified)
                                repeat = true;
                            else
                                _busy = false;
                        }
                    }
                }
            }

            public void Dispose()
            {
                if (_listening != null)
                    _listening.Dispose();
            }

            private long GetLastId(NpgsqlConnection conn)
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

            private List<EventStoreEvent> GetEvents(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "SELECT id, streamname, version, format, eventtype, contents FROM eventstore_events WHERE id > ",
                        StartingId.ToString(), " ORDER BY id LIMIT ", MaxCount.ToString());
                    using (var reader = cmd.ExecuteReader())
                    {
                        var list = new List<EventStoreEvent>(MaxCount);
                        while (reader.Read())
                        {
                            var evnt = new EventStoreEvent();
                            evnt.Token = TokenFromId(reader.GetInt64(0));
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
        }
#endif
    }
}
