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
        private IQueueExecution _executor;
        private ListeningManager _changes;
        private static EventStoreEvent[] EmptyList = new EventStoreEvent[0];

        public EventStorePostgres(DatabasePostgres db, IQueueExecution executor)
        {
            _db = db;
            _executor = executor;
            _changes = new ListeningManager(this);
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
        }

        public void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
        {
            new AddToStreamWorker(this, stream, events, expectedVersion, onComplete, onConcurrency, onError).Execute();
        }

        public void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
        {
            new ReadStreamWorker(this, stream, minVersion, maxCount, loadBody, onComplete, onError).Execute();
        }

        public void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
        {
            new LoadBodiesWorker(this, events, onComplete, onError).Execute();
        }

        public void GetAllEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            new GetAllEventsWorker(this, true, token, maxCount, loadBody, onComplete, onError).Execute();
        }

        public IDisposable WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            var worker = new GetAllEventsWorker(this, false, token, maxCount, loadBody, onComplete, onError);
            worker.Execute();
            return worker;
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

        private class Listener : IDisposable, IQueuedExecutionDispatcher
        {
            private ListeningManager _parent;
            public int Key;
            public Action Handler;

            public Listener(ListeningManager parent, int key, Action handler)
            {
                _parent = parent;
                Key = key;
                Handler = handler;
            }

            public void Execute()
            {
                Handler();
            }

            public void Dispose()
            {
                _parent.Unregister(Key);
            }
        }

        private class ListeningManager
        {
            private EventStorePostgres _parent;
            private IDisposable _listening;
            private int _listenerKey;
            private Dictionary<int, Listener> _listeners;

            public ListeningManager(EventStorePostgres parent)
            {
                _parent = parent;
                _listeners = new Dictionary<int, Listener>();
            }

            public IDisposable Register(Action handler)
            {
                lock (this)
                {
                    if (_listening == null)
                        _listening = _parent._db.Listen("eventstore", OnNotified);
                    var listener = new Listener(this, Interlocked.Increment(ref _listenerKey), handler);
                    _listeners[listener.Key] = listener;
                    return listener;
                }
            }

            public void Unregister(int key)
            {
                lock (this)
                {
                    _listeners.Remove(key);
                }
            }

            private void OnNotified()
            {
                lock (this)
                {
                    foreach (var listener in _listeners.Values)
                        _parent._executor.Enqueue(listener);
                }
            }
        }

        private class AddToStreamWorker
        {
            private EventStorePostgres _parent;
            private string _stream;
            private List<EventStoreEvent> _events;
            private EventStoreVersion _expectedVersion;
            private Action _onComplete;
            private Action _onConcurrency;
            private Action<Exception> _onError;
            private int _realVersion;

            public AddToStreamWorker(EventStorePostgres parent, string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
            {
                _parent = parent;
                _stream = stream;
                _events = events.ToList();
                _expectedVersion = expectedVersion;
                _onComplete = onComplete;
                _onConcurrency = onConcurrency;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DbExecute, _onError);
            }
            private void DbExecute(NpgsqlConnection conn)
            {
                using (var tran = conn.BeginTransaction())
                {
                    var rawVersion = GetStreamVersion(conn);
                    _realVersion = rawVersion == -1 ? 0 : rawVersion;
                    if (!_expectedVersion.VerifyVersion(_realVersion))
                    {
                        _parent._executor.Enqueue(_onConcurrency);
                        return;
                    }
                    if (rawVersion == -1)
                    {
                        if (CreateStream(conn, tran))
                        {
                            rawVersion = GetStreamVersion(conn);
                            _realVersion = rawVersion == -1 ? 0 : rawVersion;
                            if (!_expectedVersion.VerifyVersion(_realVersion))
                            {
                                _parent._executor.Enqueue(_onConcurrency);
                                return;
                            }
                        }
                    }
                    InsertNewEvents(conn);
                    UpdateStreamVersion(conn);
                    NotifyChanges(conn);
                    tran.Commit();
                    _parent._executor.Enqueue(_onComplete);
                }
            }

            private bool GetVersionAndVerify(NpgsqlConnection conn)
            {
                var rawVersion = GetStreamVersion(conn);
                _realVersion = rawVersion == -1 ? 0 : rawVersion;
                if (!_expectedVersion.VerifyVersion(_realVersion))
                {
                    _parent._executor.Enqueue(_onConcurrency);
                    return false;
                }
                else
                    return true;
            }

            private int GetStreamVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM eventstore_streams WHERE streamname = :streamname FOR UPDATE";
                    cmd.Parameters.AddWithValue("streamname", _stream);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            return reader.GetInt32(0);
                        else
                            return -1;
                    }
                }
            }

            private bool CreateStream(NpgsqlConnection conn, NpgsqlTransaction tran)
            {
                tran.Save("createstream");
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO eventstore_streams (streamname, version) VALUES (:streamname, 0)";
                        cmd.Parameters.AddWithValue("streamname", _stream);
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

            private void InsertNewEvents(NpgsqlConnection conn)
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
                    foreach (var evnt in _events)
                    {
                        evnt.StreamName = _stream;
                        evnt.StreamVersion = ++_realVersion;
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

            private void UpdateStreamVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE eventstore_streams SET version = :version WHERE streamname = :streamname";
                    cmd.Parameters.AddWithValue("streamname", _stream);
                    cmd.Parameters.AddWithValue("version", _realVersion);
                    cmd.ExecuteNonQuery();
                }
            }

            private void NotifyChanges(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "NOTIFY eventstore";
                    cmd.ExecuteNonQuery();
                }
            }
        }
        private class ReadStreamWorker
        {
            private EventStorePostgres _parent;
            private string _stream;
            private int _minVersion;
            private int _maxVersion;
            private int _maxCount;
            private bool _loadBody;
            private Action<IEventStoreStream> _onComplete;
            private Action<Exception> _onError;
            private static EventStoreEvent[] EmptyList = new EventStoreEvent[0];

            public ReadStreamWorker(EventStorePostgres parent, string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._stream = stream;
                this._minVersion = Math.Max(1, minVersion);
                this._maxCount = Math.Max(0, maxCount);
                this._maxVersion = GetMaxVersion(_minVersion, _maxCount);
                this._loadBody = loadBody;
                this._onComplete = onComplete;
                this._onError = onError;
            }

            private static int GetMaxVersion(int minVersion, int maxCount)
            {
                var maxVersion = minVersion - 1 + maxCount;
                return maxVersion >= minVersion ? maxVersion : int.MaxValue;
            }

            public void Execute()
            {
                _parent._db.Execute(DbExecute, _onError);
            }
            private void DbExecute(NpgsqlConnection conn)
            {
                var version = GetStreamVersion(conn);
                if (version < _minVersion)
                {
                    var result = new EventStoreStream(EmptyList, version, version);
                    _parent._executor.Enqueue(new EventStoreReadStreamComplete(_onComplete, result));

                }
                else if (_maxCount <= 0)
                {
                    var result = new EventStoreStream(EmptyList, version, _minVersion);
                    _parent._executor.Enqueue(new EventStoreReadStreamComplete(_onComplete, result));
                }
                else
                {
                    var events = LoadEvents(conn, _minVersion, Math.Min(version, _maxVersion));
                    var result = new EventStoreStream(events, version, _minVersion);
                    _parent._executor.Enqueue(new EventStoreReadStreamComplete(_onComplete, result));
                }
            }

            private int GetStreamVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM eventstore_streams WHERE streamname = :streamname";
                    cmd.Parameters.AddWithValue("streamname", _stream);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return 0;
                        else
                            return reader.GetInt32(0);
                    }
                }
            }

            private List<EventStoreEvent> LoadEvents(NpgsqlConnection conn, int minVersion, int maxVersion)
            {
                var expectedCount = maxVersion - minVersion + 1;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT id, streamname, version, format, eventtype, contents FROM eventstore_events " +
                        "WHERE streamname = :streamname AND version >= :minversion AND version <= :maxversion " +
                        "ORDER BY version";
                    cmd.Parameters.AddWithValue("streamname", _stream);
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
        private class LoadBodiesWorker
        {
            private EventStorePostgres _parent;
            private IList<EventStoreEvent> _events;
            private Action _onComplete;
            private Action<Exception> _onError;
            private Dictionary<long, EventStoreEvent> _ids;

            public LoadBodiesWorker(EventStorePostgres parent, IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._events = events;
                this._onComplete = onComplete;
                this._onError = onError;
            }

            public void Execute()
            {
                for (int i = 0; i < _events.Count; i++)
                {
                    var evnt = _events[i];
                    if (evnt.Body != null)
                        continue;
                    else
                    {
                        _ids = _ids ?? new Dictionary<long, EventStoreEvent>(_events.Count);
                        var id = IdFromToken(evnt.Token);
                        _ids[id] = evnt;
                    }
                }
                if (_ids == null || _ids.Count == 0)
                    _parent._executor.Enqueue(_onComplete);
                else
                    _parent._db.Execute(DbExecute, _onError);
            }

            private void DbExecute(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, contents FROM eventstore_events WHERE id = ANY (:id)";
                    var paramId = cmd.Parameters.Add("id", NpgsqlDbType.Bigint | NpgsqlDbType.Array);
                    paramId.Value = _ids;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetInt64(0);
                            var body = reader.GetString(1);
                            EventStoreEvent evnt;
                            if (_ids.TryGetValue(id, out evnt))
                                evnt.Body = body;
                        }
                    }
                    _parent._executor.Enqueue(_onComplete);
                }
            }
        }
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
    }
}
