using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Threading;

namespace Vydejna.Domain
{
    public class EventStorePostgres : IEventStoreWaitable, IDisposable
    {
        private DatabasePostgres _db;
        private IQueueExecution _executor;
        private EventsCache _cache;
        private NotificationListener _listener;

        public EventStorePostgres(DatabasePostgres db, IQueueExecution executor)
        {
            _db = db;
            _executor = executor;
            _cache = new EventsCache(this);
            _listener = new NotificationListener(this);
        }

        public void Initialize()
        {
            _db.ExecuteSync(InitializeDatabase);
        }

        public void Dispose()
        {
            _cache.Dispose();
            _listener.Dispose();
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
                    "id serial PRIMARY KEY, " +
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

        public void GetAllEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            _cache.Process(new GetAllEventsRequest(this, token, streamPrefix, eventType, maxCount, loadBody, onComplete, onError, true));
        }

        public IDisposable WaitForEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            _listener.StartListening();
            var request = new GetAllEventsRequest(this, token, streamPrefix, eventType, maxCount, loadBody, onComplete, onError, false);
            _cache.Process(request);
            return request;
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
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "INSERT INTO eventstore_streams (streamname, version) " +
                            "SELECT :streamname, 0 WHERE NOT EXISTS " +
                            "(SELECT 1 FROM eventstore_streams WHERE streamname = :streamname)";
                        cmd.Parameters.AddWithValue("streamname", _stream);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (NpgsqlException exception)
                {
                    if (exception.ErrorCode != 23505)
                        throw;
                }
                using (var tran = conn.BeginTransaction())
                {
                    int version;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT version FROM eventstore_streams WHERE streamname = :streamname LIMIT 1 FOR UPDATE";
                        cmd.Parameters.AddWithValue("streamname", _stream);
                        version = (int)cmd.ExecuteScalar();
                    }
                    bool versionVerified = _expectedVersion.Verify(version, _stream);
                    var finalVersion = version + _events.Count;
                    if (versionVerified)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "UPDATE eventstore_streams SET version = :version WHERE streamname = :streamname LIMIT 1";
                            cmd.Parameters.AddWithValue("version", finalVersion);
                            cmd.Parameters.AddWithValue("streamname", _stream);
                            cmd.ExecuteNonQuery();
                        }
                        var eventTokens = new List<int>();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = string.Format("SELECT nextval('eventstore_events_id_seq') FROM generate_series(1, {0})", _events.Count);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                    eventTokens.Add(reader.GetInt32(0));
                            }
                        }
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "INSERT INTO eventstore_events (id, streamname, version, eventtype, format, contents) " +
                                "VALUES (:token, :streamname, :version, :eventtype, :contents)";
                            var paramToken = cmd.Parameters.Add("token", NpgsqlDbType.Integer);
                            var paramName = cmd.Parameters.Add("streamname", NpgsqlDbType.Varchar);
                            var paramVersion = cmd.Parameters.Add("version", NpgsqlDbType.Integer);
                            var paramType = cmd.Parameters.Add("eventtype", NpgsqlDbType.Varchar);
                            var paramFormat = cmd.Parameters.Add("format", NpgsqlDbType.Varchar);
                            var paramContents = cmd.Parameters.Add("contents", NpgsqlDbType.Text);
                            cmd.Prepare();

                            for (int i = 0; i < _events.Count; i++)
                            {
                                _events[i].Token = new EventStoreToken(eventTokens[i].ToString());
                                paramToken.Value = eventTokens[i];
                                paramName.Value = _events[i].StreamName = _stream;
                                paramVersion.Value = _events[i].StreamVersion = version + i + 1;
                                paramType.Value = _events[i].Type;
                                paramFormat.Value = _events[i].Format;
                                paramContents.Value = _events[i].Body;
                                cmd.ExecuteNonQuery();
                            }
                        }
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "NOTIFY eventstore";
                            cmd.ExecuteNonQuery();
                        }
                        _parent._executor.Enqueue(_onComplete);
                    }
                    else
                        _parent._executor.Enqueue(_onConcurrency);
                    tran.Commit();
                }
            }
        }
        private class ReadStreamWorker
        {
            private EventStorePostgres _parent;
            private string _stream;
            private int _minVersion;
            private int _maxCount;
            private bool _loadBody;
            private Action<IEventStoreStream> _onComplete;
            private Action<Exception> _onError;

            public ReadStreamWorker(EventStorePostgres parent, string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._stream = stream;
                this._minVersion = minVersion;
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
                var events = new List<EventStoreEvent>();
                var streamVersion = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT id, streamname, version, eventtype, format, contents FROM eventstore_events " +
                        "WHERE streamname = :streamname AND version >= :minversion ORDER BY version LIMIT :maxcount";
                    cmd.Parameters.AddWithValue("streamname", _stream);
                    cmd.Parameters.AddWithValue("minversion", _minVersion);
                    cmd.Parameters.AddWithValue("maxcount", _maxCount);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var evnt = new EventStoreEvent();
                            evnt.Token = new EventStoreToken(reader.GetInt32(0).ToString());
                            evnt.StreamName = reader.GetString(1);
                            evnt.StreamVersion = reader.GetInt32(2);
                            evnt.Type = reader.GetString(3);
                            evnt.Format = reader.GetString(4);
                            evnt.Body = reader.GetString(5);
                            events.Add(evnt);
                        }
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM eventstore_streams WHERE streamname = :streamname LIMIT 1";
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (reader.Read())
                            streamVersion = (int)reader.GetInt32(0);
                    }
                }
                _parent._executor.Enqueue(new ReadStreamComplete(_onComplete, new EventStoreStream(events, streamVersion, 0)));
            }
        }
        private class ReadStreamComplete : IQueuedExecutionDispatcher
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
        private class LoadBodiesWorker
        {
            private EventStorePostgres _parent;
            private IList<EventStoreEvent> _events;
            private Action _onComplete;
            private Action<Exception> _onError;

            public LoadBodiesWorker(EventStorePostgres parent, IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._events = events;
                this._onComplete = onComplete;
                this._onError = onError;
            }

            public void Execute()
            {
                if (_events.Any(e => e.Body == null))
                    _parent._db.Execute(DbExecute, _onError);
                else
                    _parent._executor.Enqueue(_onComplete);
            }

            private void DbExecute(NpgsqlConnection conn)
            {
                var eventDirectory = _events.Where(e => e.Body == null).Select(e => new { Id = int.Parse(e.Token.ToString()), Event = e }).ToDictionary(x => x.Id, x => x.Event);
                var missingIds = eventDirectory.Keys.ToArray();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, contents FROM eventstore_events WHERE id = ANY :ids";
                    var paramIds = cmd.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer);
                    paramIds.Value = missingIds;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetInt32(0);
                            var contents = reader.GetString(1);
                            eventDirectory[id].Body = contents;
                        }
                    }
                }
                _parent._executor.Enqueue(_onComplete);
            }
        }
        private class GetAllEventsRequest : IDisposable
        {
            public int StartingId;
            public EventStoreToken PublicToken;
            public string Prefix;
            public string EventType;
            public int MaxCount;
            public bool LoadBody;
            public Action<IEventStoreCollection> OnComplete;
            public Action<Exception> OnError;
            public bool Nowait;
            private EventStorePostgres _parent;

            public GetAllEventsRequest(EventStorePostgres parent, EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError, bool nowait)
            {
                this._parent = parent;
                this.PublicToken = token;
                this.Prefix = streamPrefix;
                this.EventType = eventType;
                this.MaxCount = maxCount;
                this.LoadBody = loadBody;
                this.OnComplete = onComplete;
                this.OnError = onError;
                this.Nowait = nowait;
                this.StartingId = int.Parse(token.ToString());
            }

            public void ExecuteIndependently()
            {
                _parent._db.Execute(DbExecute, OnError);
            }

            private void DbExecute(NpgsqlConnection conn)
            {
                var events = new List<EventStoreEvent>();
                int maxId = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM eventstore_events ORDER BY id DESC LIMIT 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            maxId = reader.GetInt32(0);
                    }
                }
                if (MaxCount <= 0)
                {
                    var nextToken = maxId == 0 ? EventStoreToken.Initial : new EventStoreToken(maxId.ToString());
                    _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, events, nextToken));
                }
                else if (maxId > 0)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        var sb = new StringBuilder();
                        sb.Append("SELECT id, streamname, version, eventtype, format, contents FROM eventstore_events ");
                        sb.AppendFormat("WHERE id > :minid ");
                        cmd.Parameters.AddWithValue("minid", StartingId);
                        if (!string.IsNullOrEmpty(Prefix))
                        {
                            sb.Append("AND streamname LIKE :prefix ");
                            cmd.Parameters.AddWithValue("prefix", Prefix + '%');
                        }
                        if (!string.IsNullOrEmpty(EventType))
                        {
                            sb.Append("AND eventtype = :eventtype ");
                            cmd.Parameters.AddWithValue("eventtype", EventType);
                        }
                        sb.AppendFormat("ORDER BY id LIMIT {0}", MaxCount);
                        cmd.CommandText = sb.ToString();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var evnt = new EventStoreEvent();
                                maxId = reader.GetInt32(0);
                                evnt.Token = new EventStoreToken(maxId.ToString());
                                evnt.StreamName = reader.GetString(1);
                                evnt.StreamVersion = reader.GetInt32(2);
                                evnt.Type = reader.GetString(3);
                                evnt.Format = reader.GetString(4);
                                evnt.Body = reader.GetString(5);
                                events.Add(evnt);
                            }
                        }
                    }
                    if (events.Count > 0 || Nowait)
                        _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, events, new EventStoreToken(maxId.ToString())));
                    else
                    {
                        this.StartingId = maxId;
                        _parent._cache.AddWaiting(this);
                    }
                }
                else if (Nowait)
                    _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, events, EventStoreToken.Initial));
                else
                {
                    this.StartingId = maxId;
                    _parent._cache.AddWaiting(this);
                }
            }

            public bool TryProcess(List<EventStoreEvent> cachedEvents)
            {
                if (MaxCount <= 0)
                {
                    var lastEvent = cachedEvents.LastOrDefault();
                    var nextToken = lastEvent == null ? EventStoreToken.Initial : lastEvent.Token;
                    _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, new EventStoreEvent[0], nextToken));
                    return true;
                }
                else
                {
                    var events = cachedEvents.Where(Filter).Take(MaxCount).ToList();
                    if (events.Count > 0)
                    {
                        _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, events, events.Last().Token));
                        return true;
                    }
                    else if (Nowait)
                    {
                        _parent._executor.Enqueue(new GetAllEventsComplete(OnComplete, events, cachedEvents.Last().Token));
                        return true;
                    }
                    else
                        return false;
                }
            }

            private bool Filter(EventStoreEvent evnt)
            {
                if (PublicToken.CompareTo(evnt.Token) >= 0)
                    return false;
                if (!string.IsNullOrEmpty(Prefix) && !evnt.StreamName.StartsWith(Prefix))
                    return false;
                if (!string.IsNullOrEmpty(EventType) && !string.Equals(EventType, evnt.Type, StringComparison.Ordinal))
                    return false;
                return true;
            }

            public void Dispose()
            {
                this.Nowait = true;
                this._parent._cache.RemoveRequest(this);
            }
        }
        private class GetAllEventsComplete : IQueuedExecutionDispatcher
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
        private class EventsCache
        {
            private List<GetAllEventsRequest> _checkCacheRequests;
            private List<GetAllEventsRequest> _forFirstTryRequests;
            private List<GetAllEventsRequest> _firstTryRequests;
            private List<GetAllEventsRequest> _waitingRequests;

            private EventStorePostgres _parent;
            private object _lock;
            private List<EventStoreEvent> _events;
            private int _minAllowed, _minLoaded, _maxLoaded, _currentId, _toLoad;
            private bool _initialized, _isWorking;

            public EventsCache(EventStorePostgres parent)
            {
                _parent = parent;
                _lock = new object();
                _events = new List<EventStoreEvent>();
                _checkCacheRequests = new List<GetAllEventsRequest>();
                _forFirstTryRequests = new List<GetAllEventsRequest>();
                _forFirstTryRequests = new List<GetAllEventsRequest>();
                _waitingRequests = new List<GetAllEventsRequest>();
            }

            public void Process(GetAllEventsRequest request)
            {
                var isIndependent = false;
                lock (_lock)
                {
                    if (!_initialized)
                    {
                        _checkCacheRequests.Add(request);
                        if (!_isWorking)
                        {
                            _isWorking = true;
                            _parent._db.Execute(InitializeCache, OnError);
                        }
                    }
                    else if (request.StartingId < _minAllowed)
                        isIndependent = true;
                    else
                    {
                        _forFirstTryRequests.Add(request);
                    }
                }
                if (isIndependent)
                    request.ExecuteIndependently();
                else
                    ProcessForFirstTry();
            }

            private void InitializeCache(NpgsqlConnection conn)
            {
                var maxId = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM eventstore_events ORDER BY id DESC LIMIT 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            maxId = reader.GetInt32(0);
                    }
                }
                var independentRequests = new List<GetAllEventsRequest>();
                lock (_lock)
                {
                    _currentId = maxId;
                    _minAllowed = Math.Max(1, maxId - 100);
                    foreach (var request in _checkCacheRequests)
                    {
                        if (request.StartingId < _minAllowed)
                            independentRequests.Add(request);
                        else
                            _forFirstTryRequests.Add(request);
                    }
                    _checkCacheRequests.Clear();
                    _initialized = true;
                    _isWorking = false;
                }
                foreach (var request in independentRequests)
                    request.ExecuteIndependently();
                ProcessForFirstTry();
            }

            private void ProcessForFirstTry()
            {
                bool needsLoading = false;
                lock (_lock)
                {
                    if (_isWorking || _forFirstTryRequests.Count == 0)
                        return;
                    _isWorking = true;
                    _toLoad = _forFirstTryRequests[0].StartingId + 1;
                    foreach (var request in _forFirstTryRequests)
                    {
                        if (request.StartingId < _toLoad)
                            _toLoad = request.StartingId + 1;
                    }
                    _firstTryRequests = _forFirstTryRequests;
                    _forFirstTryRequests = new List<GetAllEventsRequest>();
                    if (_minLoaded == 0 || _toLoad < _minLoaded || _maxLoaded < _currentId)
                        needsLoading = true;
                }
                if (needsLoading)
                    _parent._db.Execute(DbFillCache, OnError);
                else
                    ProcessCache(false);
            }

            public void NotifyChange()
            {
                lock (_lock)
                {
                    if (_isWorking)
                        return;
                    _isWorking = true;
                    _parent._db.Execute(DbRefreshCache, OnError);
                }
            }

            private void DbRefreshCache(NpgsqlConnection conn)
            {
                var maxId = _currentId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM eventstore_events ORDER BY id DESC LIMIT 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            maxId = reader.GetInt32(0);
                    }
                }
                _currentId = maxId;
                _minAllowed = Math.Max(1, _currentId - 100);
                _minLoaded = Math.Max(_minAllowed, _minLoaded);
                var minToken = new EventStoreToken(_minLoaded.ToString());
                _events.RemoveAll(e => minToken.CompareTo(e.Token) > 0);
                if (_minLoaded == 0)
                {
                    _minLoaded = _toLoad;
                    _maxLoaded = _currentId;
                    _events.AddRange(LoadEventsInRange(conn, _minLoaded, _maxLoaded));
                }
                else
                {
                    if (_toLoad < _minLoaded)
                    {
                        _minLoaded = _toLoad;
                        _events.InsertRange(0, LoadEventsInRange(conn, _toLoad, _minLoaded - 1));
                    }
                    if (_maxLoaded < _currentId)
                    {
                        _maxLoaded = _currentId;
                        _events.AddRange(LoadEventsInRange(conn, _maxLoaded + 1, _currentId));
                    }
                }
                ProcessCache(true);
            }

            private void DbFillCache(NpgsqlConnection conn)
            {
                if (_minLoaded == 0)
                {
                    _minLoaded = _toLoad;
                    _maxLoaded = _currentId;
                    _events.AddRange(LoadEventsInRange(conn, _minLoaded, _maxLoaded));
                }
                else
                {
                    if (_toLoad < _minLoaded)
                    {
                        _minLoaded = _toLoad;
                        _events.InsertRange(0, LoadEventsInRange(conn, _toLoad, _minLoaded - 1));
                    }
                    if (_maxLoaded < _currentId)
                    {
                        _maxLoaded = _currentId;
                        _events.AddRange(LoadEventsInRange(conn, _maxLoaded + 1, _currentId));
                    }
                }
                ProcessCache(false);
            }

            private IEnumerable<EventStoreEvent> LoadEventsInRange(NpgsqlConnection conn, int minId, int maxId)
            {
                var events = new List<EventStoreEvent>(maxId - minId + 1);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT id, streamname, version, eventtype, format, contents FROM eventstore_events " +
                        "WHERE id >= :minid AND id <= :maxid ORDER BY id";
                    cmd.Parameters.AddWithValue("minid", minId);
                    cmd.Parameters.AddWithValue("maxid", maxId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var evnt = new EventStoreEvent();
                            evnt.Token = new EventStoreToken(reader.GetInt32(0).ToString());
                            evnt.StreamName = reader.GetString(1);
                            evnt.StreamVersion = reader.GetInt32(2);
                            evnt.Type = reader.GetString(3);
                            evnt.Format = reader.GetString(4);
                            evnt.Body = reader.GetString(5);
                            events.Add(evnt);
                        }
                    }
                }
                return events;
            }

            private void ProcessCache(bool includingWaiting)
            {
                lock (_lock)
                {
                    var forWaiting = includingWaiting ? new List<GetAllEventsRequest>() : _waitingRequests;
                    foreach (var request in _firstTryRequests)
                    {
                        if (!request.TryProcess(_events))
                            forWaiting.Add(request);
                    }
                    if (includingWaiting)
                    {
                        foreach (var request in _waitingRequests)
                        {
                            if (!request.TryProcess(_events))
                                forWaiting.Add(request);
                        }
                        _waitingRequests = forWaiting;
                    }
                    _isWorking = false;
                }
                ProcessForFirstTry();
            }

            private void OnError(Exception exception)
            {
                var allRequests = new List<GetAllEventsRequest>();
                lock (_lock)
                {
                    _initialized = false;
                    _isWorking = false;
                    _events.Clear();
                    _minLoaded = _maxLoaded = 0;
                    allRequests.AddRange(_checkCacheRequests);
                    allRequests.AddRange(_forFirstTryRequests);
                    allRequests.AddRange(_firstTryRequests);
                    allRequests.AddRange(_waitingRequests);
                    _checkCacheRequests.Clear();
                    _forFirstTryRequests.Clear();
                    _firstTryRequests.Clear();
                    _waitingRequests.Clear();
                }
                foreach (var request in allRequests)
                    _parent._executor.Enqueue(request.OnError, exception);
            }

            public void AddWaiting(GetAllEventsRequest request)
            {
                lock (_lock)
                    _waitingRequests.Add(request);
            }

            public void RemoveRequest(GetAllEventsRequest request)
            {
                lock (_lock)
                {
                    _forFirstTryRequests.Remove(request);
                    _firstTryRequests.Remove(request);
                    _waitingRequests.Remove(request);
                }
                request.TryProcess(new List<EventStoreEvent>());
            }

            public void Dispose()
            {
                var allRequests = new List<GetAllEventsRequest>();
                lock (_lock)
                {
                    _initialized = false;
                    _isWorking = false;
                    _events.Clear();
                    _minLoaded = _maxLoaded = 0;
                    allRequests.AddRange(_checkCacheRequests);
                    allRequests.AddRange(_forFirstTryRequests);
                    allRequests.AddRange(_firstTryRequests);
                    allRequests.AddRange(_waitingRequests);
                    _checkCacheRequests.Clear();
                    _forFirstTryRequests.Clear();
                    _firstTryRequests.Clear();
                    _waitingRequests.Clear();
                }
                var noEvents = new List<EventStoreEvent>();
                foreach (var request in allRequests)
                {
                    request.Nowait = true;
                    request.TryProcess(noEvents);
                }
            }
        }
        private class NotificationListener : IDisposable
        {
            private EventStorePostgres _parent;
            private object _lock;
            private bool _isListening;
            private NpgsqlConnection _conn;
            private Timer _timer;

            public NotificationListener(EventStorePostgres parent)
            {
                _parent = parent;
                _lock = new object();
            }

            public void StartListening()
            {
                lock (_lock)
                {
                    if (_isListening)
                        return;
                    _isListening = true;
                    if (_timer == null)
                        _timer = new Timer(OnTimer, null, 1000, 1000);
                }
                _parent._db.OpenConnection(OnConnected, OnError);
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_timer != null)
                        _timer.Dispose();
                    _timer = null;
                }
                StopListening();
            }

            private void OnError(Exception exception)
            {
                StopListening();
            }

            private void OnTimer(object state)
            {
                _parent._cache.NotifyChange();
            }

            private void OnConnected(NpgsqlConnection conn)
            {
                lock (_lock)
                {
                    _conn = conn;
                    _conn.StateChange += ConnectionStateChanged;
                    _conn.Notification += ConnectionNotification;
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "LISTEN eventstore";
                    cmd.ExecuteNonQuery();
                }
            }

            private void ConnectionNotification(object sender, NpgsqlNotificationEventArgs e)
            {
                _parent._cache.NotifyChange();
            }

            private void ConnectionStateChanged(object sender, StateChangeEventArgs e)
            {
                StopListening();
            }

            private void StopListening()
            {
                lock (_lock)
                {
                    if (_conn != null)
                    {
                        _conn.StateChange -= ConnectionStateChanged;
                        _conn.Notification -= ConnectionNotification;
                        _parent._db.ReleaseConnection(_conn);
                        _conn = null;
                        _isListening = false;
                    }
                }
            }
        }
    }
}
