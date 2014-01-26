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

        public EventStorePostgres(DatabasePostgres db, IQueueExecution executor)
        {
            _db = db;
            _executor = executor;
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
            return new EventStoreToken(id.ToString());
        }
        private static long IdFromToken(EventStoreToken token)
        {
            if (token.IsInitial)
                return 0;
            else if (token.IsCurrent)
                return long.MaxValue;
            else
                return long.Parse(token.ToString());
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
                        if (CreateStream(conn))
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
                    _realVersion = InsertNewEvents(conn);
                    UpdateStreamVersion(conn);
                    tran.Commit();
                }
                /*
                 * Get stream version and lock
                 * Verify version
                 * If stream does not exist, create and lock it (use savepoint)
                 * In case of conflict verify version again
                 * Insert new events
                 * Update stream version
                 * Commit
                 */
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
                this._maxVersion = this._minVersion - 1 + Math.Max(0, maxCount);
                this._maxCount = maxCount;
                this._loadBody = loadBody;
                this._onComplete = onComplete;
                this._onError = onError;
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
                        var list = new List<EventStoreEvent(expectedCount);
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
                }
            }
        }
        private class GetAllEventsWorker : IDisposable
        {
            public int StartingId;
            public EventStoreToken PublicToken;
            public int MaxCount;
            public bool LoadBody;
            public Action<IEventStoreCollection> OnComplete;
            public Action<Exception> OnError;
            public bool Nowait;
            private EventStorePostgres _parent;

            public GetAllEventsWorker(EventStorePostgres parent, bool nowait, EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this.PublicToken = token;
                this.MaxCount = maxCount;
                this.LoadBody = loadBody;
                this.OnComplete = onComplete;
                this.OnError = onError;
                this.Nowait = nowait;
                this.StartingId = token.IsInitial ? 0 : int.Parse(token.ToString());
            }

            public void Execute()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
