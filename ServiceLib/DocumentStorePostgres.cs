﻿using System;
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
        private string _partition;

        private class Folder : IDocumentFolder
        {
            private DocumentStorePostgres _parent;
            private string _folderName;

            public Folder(DocumentStorePostgres parent, string folderName)
            {
                _parent = parent;
                _folderName = folderName;
            }

            public void DeleteAll(Action onComplete, Action<Exception> onError)
            {
                new DeleteAllWorker(_parent, _folderName, onComplete, onError).Execute();
            }

            public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new GetDocumentWorker(_parent, _folderName, name, 0, onFound, null, onMissing, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new GetDocumentWorker(_parent, _folderName, name, knownVersion, onFoundNewer, onNotModified, onMissing, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new SaveDocumentWorker(_parent, _folderName, name, value, expectedVersion, onSave, onConcurrency, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }
        }

        public DocumentStorePostgres(DatabasePostgres db, IQueueExecution executor, string partition)
        {
            _db = db;
            _executor = executor;
            _pathRegex = new Regex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);
            _partition = partition;
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
                cmd.CommandText = string.Concat(
                    "CREATE TABLE IF NOT EXISTS ", _partition,
                    " (folder varchar NOT NULL, document varchar NOT NULL, version integer NOT NULL, contents text NOT NULL, PRIMARY KEY (folder, document))");
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "CREATE TABLE IF NOT EXISTS ", _partition,
                    "_idx (folder varchar NOT NULL, value varchar NOT NULL, document varchar NOT NULL, PRIMARY KEY (folder, value, document))");
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat("CREATE INDEX ON ", _partition, "_idx (folder, document)");
                cmd.ExecuteNonQuery();
            }
        }

        private bool VerifyPath(string path)
        {
            return _pathRegex.IsMatch(path);
        }

        public IDocumentFolder GetFolder(string name)
        {
            return new Folder(this, name);
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
                    cmd.CommandText = string.Concat("DELETE FROM ", _parent._partition, " WHERE key LIKE '", _path, "%'; NOTIFY ", _parent._partition, ";");
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
                    cmd.CommandText = "SELECT version, contents FROM " + _parent._partition + " WHERE key = :key LIMIT 1";
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
                    cmd.CommandText = "SELECT version FROM " + _parent._partition + " WHERE key = :key LIMIT 1";
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

            private void DoWork(NpgsqlConnection conn)
            {
                var key = string.Concat(_path, "/", _name);
                bool retry = true;
                int documentVersion = -1;
                bool wasSaved = false;
                while (retry)
                {
                    retry = false;
                    try
                    {
                        using (var tran = conn.BeginTransaction())
                        {

                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "SELECT version FROM " + _parent._partition + " WHERE key = :key LIMIT 1 FOR UPDATE";
                                cmd.Parameters.AddWithValue("key", key);
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                        documentVersion = reader.GetInt32(0);
                                    else
                                        documentVersion = -1;
                                }
                            }

                            if (documentVersion == -1)
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "INSERT INTO " + _parent._partition + " (key, version, contents) VALUES (:key, 1, :contents)";
                                    cmd.Parameters.AddWithValue("key", key);
                                    cmd.Parameters.AddWithValue("contents", _value);
                                    cmd.ExecuteNonQuery();
                                }
                                wasSaved = true;
                                tran.Commit();
                            }
                            else if (_expectedVersion.VerifyVersion(documentVersion))
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "UPDATE " + _parent._partition + " SET version = :version, contents = :contents WHERE key = :key";
                                    cmd.Parameters.AddWithValue("key", key);
                                    cmd.Parameters.AddWithValue("version", documentVersion + 1);
                                    cmd.Parameters.AddWithValue("contents", _value);
                                    cmd.ExecuteNonQuery();
                                }
                                wasSaved = true;
                                tran.Commit();
                            }
                            else
                                wasSaved = false;
                        }
                    }
                    catch (NpgsqlException ex)
                    {
                        if (ex.Code == "23505")
                            retry = true;
                        else
                            throw;
                    }
                }

                if (wasSaved)
                    _parent._executor.Enqueue(_onSave);
                else
                    _parent._executor.Enqueue(_onConcurrency);
            }
        }
    }

}
