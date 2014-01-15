using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;
using System.Threading;

namespace ServiceLib
{
    public class DocumentStorePostgres : IDocumentStore, IDisposable
    {
        private IQueueExecution _executor;
        private Folder _root;
        private DatabasePostgres _db;
        private Regex _pathRegex;
        private object _lock;

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
                    new GetDocumentWorker(_parent, _path, name, 0, onFound, null, onMissing, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new GetDocumentWorker(_parent, _path, name, knownVersion, onFoundNewer, onNotModified, onMissing, onError).Execute();
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
        }

        public DocumentStorePostgres(DatabasePostgres db, IQueueExecution executor)
        {
            _db = db;
            _executor = executor;
            _root = new Folder(this, "");
            _pathRegex = new Regex(@"^[a-zA-Z0-9\-._/]+$", RegexOptions.Compiled);
            _lock = new object();
        }

        public void Dispose()
        {
        }

        public void Initialize()
        {
            _db.ExecuteSync(InitializeDatabase);
        }
        private void InitializeDatabase(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS documents (key varchar PRIMARY KEY, version integer NOT NULL, contents text NOT NULL)";
                cmd.ExecuteNonQuery();
            }
        }

        private bool VerifyPath(string path)
        {
            return _pathRegex.IsMatch(path);
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

        public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
        {
            _root.GetNewerDocument(name, knownVersion, onFoundNewer, onNotModified, onMissing, onError);
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
                    cmd.CommandText = string.Concat("DELETE FROM documents WHERE key LIKE '", _path, "%'; NOTIFY documents;");
                    cmd.ExecuteNonQuery();
                    _parent._executor.Enqueue(_onComplete);
                }
            }
        }

        private class GetDocumentWorker
        {
            private DocumentStorePostgres _parent;
            private string _path;
            private string _name;
            private int _knownVersion;
            private Action<int, string> _onFound;
            private Action _onNotModified;
            private Action _onMissing;
            private Action<Exception> _onError;

            public GetDocumentWorker(DocumentStorePostgres parent, string path, string name, int knownVersion, Action<int, string> onFound, Action onNotModified, Action onMissing, Action<Exception> onError)
            {
                _parent = parent;
                _path = path;
                _name = name;
                _knownVersion = knownVersion;
                _onFound = onFound;
                _onNotModified = onNotModified;
                _onMissing = onMissing;
                _onError = onError;
            }

            public void Execute()
            {
                if (_onNotModified == null)
                    _parent._db.Execute(DoWorkWithoutVersion, _onError);
                else
                    _parent._db.Execute(DoWorkWithVersion, _onError);
            }

            private void DoWorkWithoutVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    var key = string.Concat(_path, "/", _name);
                    cmd.CommandText = "SELECT version, contents FROM documents WHERE key = :key LIMIT 1";
                    cmd.Parameters.AddWithValue("key", key);
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

            private void DoWorkWithVersion(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    var key = string.Concat(_path, "/", _name);
                    cmd.CommandText = "SELECT version FROM documents WHERE key = :key LIMIT 1";
                    cmd.Parameters.AddWithValue("key", key);
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        int version;
                        if (!reader.Read())
                        {
                            _parent._executor.Enqueue(_onMissing);
                            return;
                        }
                        else
                        {
                            version = reader.GetInt32(0);
                            if (version == _knownVersion)
                            {
                                _parent._executor.Enqueue(_onNotModified);
                                return;
                            }
                        }
                    }
                }
                DoWorkWithoutVersion(conn);
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
                var key = string.Concat(_path, "/", _name);
                var lockKey = GetLockKey(key);
                int documentVersion;
                bool wasSaved;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT pg_advisory_lock(:lock)";
                    cmd.Parameters.AddWithValue("lock", lockKey);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM documents WHERE key = :key LIMIT 1";
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
                            cmd.CommandText = "INSERT INTO documents (key, version, contents) VALUES (:key, :version, :contents)";
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
    }

}
