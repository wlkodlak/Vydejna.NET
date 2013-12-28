using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;
using System.Threading;

namespace Vydejna.Domain
{
    public class DocumentStorePostgres : IDocumentStore, IDisposable
    {
        private IQueueExecution _executor;
        private Dictionary<int, Watcher> _watchers;
        private Folder _root;
        private int _watcherKey;
        private DatabasePostgres _db;
        private Regex _pathRegex;
        private object _lock;
        private NpgsqlConnection _listeningConnection;
        private bool _isListening;
        private bool _isCheckingChanges;
        private Timer _timer;

        private class Folder : IDocumentFolder
        {
            private DocumentStorePostgres _parent;
            private string _path;
            
            public Folder(DocumentStorePostgres parent, string path)
            {
                _parent = parent;
                _path = path;
            }

            public IDocumentFolder SubFolder(string name)
            {
                if (!_parent.VerifyPath(name))
                    throw new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name));
                return new Folder(_parent, _path + "/" + name);
            }

            public void DeleteAll(Action onComplete, Action<Exception> onError)
            {
                new DeleteAllWorker(_parent, _path, onComplete, onError).Execute();
            }

            public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new GetDocumentWorker(_parent, _path, name, onFound, onMissing, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new SaveDocumentWorker(_parent, _path, name, value, expectedVersion, onSave, onConcurrency, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public IDisposable WatchChanges(string name, Action onSomethingChanged)
            {
                if (!_parent.VerifyPath(name))
                    throw new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name));
                var watcher = new Watcher(_parent, _parent.GetWatcherKey(), _path + "/" + name, onSomethingChanged);
                watcher.StartWatching();
                return watcher;
            }
        }

        public DocumentStorePostgres(DatabasePostgres db, IQueueExecution executor)
        {
            _db = db;
            _executor = executor;
            _watchers = new Dictionary<int, Watcher>();
            _root = new Folder(this, "");
            _pathRegex = new Regex(@"^[a-zA-Z0-9\-._/]+$", RegexOptions.Compiled);
            _lock = new object();
            _timer = new Timer(o => StartCheckingChanges(), null, 30000, 30000);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private int GetWatcherKey()
        {
            return System.Threading.Interlocked.Increment(ref _watcherKey);
        }

        private bool VerifyPath(string path)
        {
            return _pathRegex.IsMatch(path);
        }

        private void StartListening()
        {
            lock (_lock)
            {
                if (_isListening)
                    return;
                _isListening = true;
            }
            _db.OpenConnection(OnListenerConnected, OnListenerError);
        }
        private void OnListenerConnected(NpgsqlConnection conn)
        {
            try
            {
                lock (_lock)
                {
                    _listeningConnection = conn;
                    _listeningConnection.StateChange += OnListenerStateChanged;
                    _listeningConnection.Notification += OnListenerNotification;
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "LISTEN documents";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception exception)
            {
                OnListenerError(exception);
            }
        }
        private void ReleaseListeningConnection()
        {
            lock (_lock)
            {
                if (_listeningConnection == null)
                    return;
                _listeningConnection.StateChange -= OnListenerStateChanged;
                _listeningConnection.Notification -= OnListenerNotification;
                _db.ReleaseConnection(_listeningConnection);
                _isListening = false;
                _listeningConnection = null;
            }
        }
        private void OnListenerError(Exception exception)
        {
            ReleaseListeningConnection();
        }
        private void OnListenerStateChanged(object sender, StateChangeEventArgs e)
        {
            if (e.CurrentState == ConnectionState.Broken)
                ReleaseListeningConnection();
        }
        private void OnListenerNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            if (e.Condition == "documents")
                _executor.Enqueue(StartCheckingChanges);
        }
        private void StartCheckingChanges()
        {
            lock (_lock)
            {
                if (_watchers.Count == 0 || _isCheckingChanges)
                    return;
                _isCheckingChanges = true;
            }
            _db.Execute(CheckChanges, CheckChangesError);
        }
        private void CheckChanges(NpgsqlConnection conn)
        {
            var byKey = GetReadyWatchers();
            var keys = byKey.Select(l => l.Key).ToArray();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT key, version FROM documents WHERE key = ANY :keys";
                var paramKeys = cmd.Parameters.Add("keys", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                paramKeys.Value = keys;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var version = reader.GetInt32(1);
                        foreach (var watcher in byKey[key])
                            watcher.NotifyVersion(version);
                    }
                }
            }
        }

        private ILookup<string, Watcher> GetReadyWatchers()
        {
            lock (_lock)
                return _watchers.Values.Where(w => w.IsReady).ToLookup(w => w.Key);
        }
        private void CheckChangesError(Exception exception)
        {
            lock (_lock)
            {
                _isCheckingChanges = false;
            }
        }

        public IDocumentFolder SubFolder(string name)
        {
            return _root.SubFolder(name);
        }

        public void DeleteAll(Action onComplete, Action<Exception> onError)
        {
            _root.DeleteAll(onComplete, onError);
        }

        public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
        {
            _root.GetDocument(name, onFound, onMissing, onError);
        }

        public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
        {
            _root.SaveDocument(name, value, expectedVersion, onSave, onConcurrency, onError);
        }

        public IDisposable WatchChanges(string name, Action onSomethingChanged)
        {
            return _root.WatchChanges(name, onSomethingChanged);
        }

        private class DeleteAllWorker
        {
            private DocumentStorePostgres _parent;
            private string _path;
            private Action _onComplete;
            private Action<Exception> _onError;

            public DeleteAllWorker(DocumentStorePostgres parent, string path, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _path = path;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DoWork, _onError);
            }

            private void DoWork(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Join("DELETE FROM documents WHERE key LIKE '", _path, "%'; NOTIFY documents;");
                    cmd.ExecuteNonQuery();
                    _parent._executor.Enqueue(_onComplete);
                }
            }
        }

        private class DocumentFound : IQueuedExecutionDispatcher
        {
            private Action<int, string> _onFound;
            private int _version;
            private string _contents;

            public DocumentFound(Action<int, string> onFound, int version, string contents)
            {
                this._onFound = onFound;
                this._version = version;
                this._contents = contents;
            }

            public void Execute()
            {
                _onFound(_version, _contents);
            }
        }

        private class GetDocumentWorker
        {
            private DocumentStorePostgres _parent;
            private string _path;
            private string _name;
            private Action<int, string> _onFound;
            private Action _onMissing;
            private Action<Exception> _onError;

            public GetDocumentWorker(DocumentStorePostgres parent, string path, string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                _parent = parent;
                _path = path;
                _name = name;
                _onFound = onFound;
                _onMissing = onMissing;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DoWork, _onError);
            }

            private void DoWork(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version, contents FROM documents WHERE key = :key LIMIT 1";
                    cmd.Parameters.AddWithValue("key", _path);
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        int version;
                        string contents;
                        if (reader.Read())
                        {
                            version = reader.GetInt32(0);
                            contents = reader.GetString(1);
                            _parent._executor.Enqueue(new DocumentFound(_onFound, version, contents));
                        }
                        else
                            _parent._executor.Enqueue(_onMissing);
                    }
                }
            }
        }

        private class SaveDocumentWorker
        {
            private DocumentStorePostgres _parent;
            private string _path;
            private string _name;
            private string _value;
            private DocumentStoreVersion _expectedVersion;
            private Action _onSave;
            private Action _onConcurrency;
            private Action<Exception> _onError;

            public SaveDocumentWorker(DocumentStorePostgres parent, string path, string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                _parent = parent;
                _path = path;
                _name = name;
                _value = value;
                _expectedVersion = expectedVersion;
                _onSave = onSave;
                _onConcurrency = onConcurrency;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DoWork, _onError);
            }

            private static long GetLockKey(string key)
            {
                unchecked
                {
                    var bytes = Encoding.ASCII.GetBytes(key);
                    uint result = 0;
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        result = (result << 11) | (result >> 21);
                        result |= bytes[i];
                    }
                    return (long)result;
                }
            }

            private void DoWork(NpgsqlConnection conn)
            {
                var key = string.Join(_path, "/", _name);
                var lockKey = GetLockKey(key);
                int documentVersion;
                bool wasSaved;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT pg_advisory_lock(:lock); SELECT version FROM documents WHERE key = :key LIMIT 1;";
                    cmd.Parameters.AddWithValue("lock", lockKey);
                    cmd.Parameters.AddWithValue("key", key);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            documentVersion = reader.GetInt32(0);
                        else
                            documentVersion = 0;
                    }
                }
                try
                {
                    if (documentVersion == 0)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO documents (key, version, contents) VALUES (:key, :version, :contents);";
                            cmd.Parameters.AddWithValue("key", key);
                            cmd.Parameters.AddWithValue("version", 1);
                            cmd.Parameters.AddWithValue("contents", _value);
                            cmd.ExecuteNonQuery();
                        }
                        wasSaved = true;
                    }
                    else if (_expectedVersion.VerifyVersion(documentVersion))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "UPDATE documents SET version = :version, contents = :contents WHERE key = :key";
                            cmd.Parameters.AddWithValue("key", key);
                            cmd.Parameters.AddWithValue("version", documentVersion + 1);
                            cmd.Parameters.AddWithValue("contents", _value);
                            cmd.ExecuteNonQuery();
                        }
                        wasSaved = true;
                    }
                    else
                        wasSaved = false;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT pg_advisory_unlock(:lock); NOTIFY documents;";
                        cmd.Parameters.AddWithValue("lock", lockKey);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT pg_advisory_unlock(:lock)";
                        cmd.Parameters.AddWithValue("lock", lockKey);
                        cmd.ExecuteNonQuery();
                    }
                    throw;
                }
                if (wasSaved)
                    _parent._executor.Enqueue(_onSave);
                else
                    _parent._executor.Enqueue(_onConcurrency);
            }
        }

        private class Watcher : IDisposable
        {
            private DocumentStorePostgres _parent;
            private string _documentName;
            private Action _callback;
            private int _key;
            private int _version;
            private bool _isReady;

            public bool IsReady { get { return _isReady; } }
            public string Key { get { return _documentName; } }

            public Watcher(DocumentStorePostgres parent, int key, string documentName, Action callback)
            {
                _parent = parent;
                _key = key;
                _documentName = documentName;
                _callback = callback;
            }

            public void Dispose()
            {
                lock (_parent._lock)
                {
                    _isReady = false;
                    _parent._watchers.Remove(_key);
                }
            }

            public void StartWatching()
            {
                _parent._db.Execute(GetVersion, OnError);
            }

            private void GetVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM documents WHERE key = :key LIMIT 1";
                    cmd.Parameters.AddWithValue("key", _documentName);
                    _version = (int)cmd.ExecuteScalar();
                    _isReady = true;
                }
            }

            private void OnError(Exception exception)
            {
            }

            public void NotifyVersion(int newVersion)
            {
                if (_version < newVersion)
                {
                    _version = newVersion;
                    _parent._executor.Enqueue(_callback);
                }
            }
        }

    }

}
