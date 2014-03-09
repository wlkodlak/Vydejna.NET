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

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                if (_parent.VerifyPath(name))
                    new SaveDocumentWorker(_parent, _folderName, name, value, expectedVersion, indexes, onSave, onConcurrency, onError).Execute();
                else
                    onError(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public void FindDocumentKeys(string indexName, string minValue, string maxValue, Action<IList<string>> onFoundKeys, Action<Exception> onError)
            {
                new FindDocumentsWorker(_parent, _folderName, indexName, minValue, maxValue, onFoundKeys, onError).Execute();
            }


            public void FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending, Action<DocumentStoreFoundDocuments> onFoundDocuments, Action<Exception> onError)
            {
                throw new NotImplementedException();
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
            var tables = new HashSet<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'r' AND n.nspname = 'public'";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        tables.Add(reader.GetString(0));
                }
            }
            if (!tables.Contains(_partition))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "CREATE TABLE IF NOT EXISTS ", _partition,
                        " (folder varchar NOT NULL, document varchar NOT NULL, version integer NOT NULL, contents text NOT NULL, PRIMARY KEY (folder, document))");
                    cmd.ExecuteNonQuery();
                }
            }
            if (!tables.Contains(_partition + "_idx"))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "CREATE TABLE IF NOT EXISTS ", _partition,
                        "_idx (folder varchar NOT NULL, indexname varchar NOT NULL, value varchar NOT NULL, document varchar NOT NULL, PRIMARY KEY (folder, indexname, value, document))");
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat("CREATE INDEX ON ", _partition, "_idx (folder, document, indexname)");
                    cmd.ExecuteNonQuery();
                }
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
            private string _folderName;
            private Action _onComplete;
            private Action<Exception> _onError;

            public DeleteAllWorker(DocumentStorePostgres parent, string folderName, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _folderName = folderName;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DoWork, _onError);
            }

            private void DoWork(NpgsqlConnection conn)
            {
                using (var tran = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Concat(
                            "DELETE FROM ", _parent._partition, " WHERE folder = '", _folderName, "'; ",
                            "DELETE FROM ", _parent._partition, "_idx WHERE folder = '", _folderName, "'; ",
                            "NOTIFY ", _parent._partition, ";");
                        cmd.ExecuteNonQuery();
                        _parent._executor.Enqueue(_onComplete);
                    }
                    tran.Commit();
                }
            }
        }

        private class GetDocumentWorker
        {
            private DocumentStorePostgres _parent;
            private string _folderName;
            private string _name;
            private int _knownVersion;
            private Action<int, string> _onFound;
            private Action _onNotModified;
            private Action _onMissing;
            private Action<Exception> _onError;

            public GetDocumentWorker(DocumentStorePostgres parent, string folderName, string name, int knownVersion, Action<int, string> onFound, Action onNotModified, Action onMissing, Action<Exception> onError)
            {
                _parent = parent;
                _folderName = folderName;
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
                    cmd.CommandText = "SELECT version, contents FROM " + _parent._partition + " WHERE folder = :folder AND document = :document LIMIT 1";
                    cmd.Parameters.AddWithValue("folder", _folderName);
                    cmd.Parameters.AddWithValue("document", _name);
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
                    cmd.CommandText = "SELECT version FROM " + _parent._partition + " WHERE folder = :folder AND document = :document LIMIT 1";
                    cmd.Parameters.AddWithValue("folder", _folderName);
                    cmd.Parameters.AddWithValue("document", _name);
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
            private string _folderName;
            private string _name;
            private string _value;
            private DocumentStoreVersion _expectedVersion;
            private IList<DocumentIndexing> _indexes;
            private Action _onSave;
            private Action _onConcurrency;
            private Action<Exception> _onError;

            public SaveDocumentWorker(DocumentStorePostgres parent, string folderName, string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                _parent = parent;
                _folderName = folderName;
                _name = name;
                _value = value;
                _expectedVersion = expectedVersion;
                _indexes = indexes;
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
                                cmd.CommandText = "SELECT version FROM " + _parent._partition + " WHERE folder = :folder AND document = :document LIMIT 1 FOR UPDATE";
                                cmd.Parameters.AddWithValue("folder", _folderName);
                                cmd.Parameters.AddWithValue("document", _name);
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
                                    cmd.CommandText = "INSERT INTO " + _parent._partition + " (folder, document, version, contents) VALUES (:folder, :document, 1, :contents)";
                                    cmd.Parameters.AddWithValue("folder", _folderName);
                                    cmd.Parameters.AddWithValue("document", _name);
                                    cmd.Parameters.AddWithValue("contents", _value);
                                    cmd.ExecuteNonQuery();
                                }
                                if (_indexes != null)
                                {
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = "INSERT INTO " + _parent._partition + "_idx (folder, document, indexname, value) SELECT :folder, :document, :indexname, unnest(:value)";
                                        cmd.Parameters.AddWithValue("folder", _folderName);
                                        cmd.Parameters.AddWithValue("document", _name);
                                        var paramIndex = cmd.Parameters.Add("indexname", NpgsqlTypes.NpgsqlDbType.Varchar);
                                        var paramValues = cmd.Parameters.Add("value", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                                        foreach (var indexChange in _indexes)
                                        {
                                            paramIndex.Value = indexChange.IndexName;
                                            paramValues.Value = indexChange.Values.Distinct().ToArray();
                                            cmd.ExecuteNonQuery();
                                        }
                                    }
                                }
                                wasSaved = true;
                                tran.Commit();
                            }
                            else if (_expectedVersion.VerifyVersion(documentVersion))
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "UPDATE " + _parent._partition + " SET version = :version, contents = :contents WHERE folder = :folder AND document = :document";
                                    cmd.Parameters.AddWithValue("folder", _folderName);
                                    cmd.Parameters.AddWithValue("document", _name);
                                    cmd.Parameters.AddWithValue("version", documentVersion + 1);
                                    cmd.Parameters.AddWithValue("contents", _value);
                                    cmd.ExecuteNonQuery();
                                }
                                if (_indexes != null)
                                {
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = "DELETE FROM " + _parent._partition + "_idx WHERE folder = :folder AND document = :document";
                                        cmd.Parameters.AddWithValue("folder", _folderName);
                                        cmd.Parameters.AddWithValue("document", _name);
                                        cmd.ExecuteNonQuery();
                                    }
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = "INSERT INTO " + _parent._partition + "_idx (folder, document, indexname, value) SELECT :folder, :document, :indexname, unnest(:value)";
                                        cmd.Parameters.AddWithValue("folder", _folderName);
                                        cmd.Parameters.AddWithValue("document", _name);
                                        var paramIndex = cmd.Parameters.Add("indexname", NpgsqlTypes.NpgsqlDbType.Varchar);
                                        var paramValues = cmd.Parameters.Add("value", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                                        foreach (var indexChange in _indexes)
                                        {
                                            paramIndex.Value = indexChange.IndexName;
                                            paramValues.Value = indexChange.Values.Distinct().ToArray();
                                            cmd.ExecuteNonQuery();
                                        }
                                    }
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

        private class FindDocumentsWorker
        {
            private DocumentStorePostgres _parent;
            private string _folderName;
            private string _indexName;
            private string _minValue;
            private string _maxValue;
            private Action<IList<string>> _onFoundKeys;
            private Action<Exception> _onError;

            public FindDocumentsWorker(DocumentStorePostgres parent, string folderName, string indexName, string minValue, string maxValue, Action<IList<string>> onFoundKeys, Action<Exception> onError)
            {
                _parent = parent;
                _folderName = folderName;
                _indexName = indexName;
                _minValue = minValue;
                _maxValue = maxValue;
                _onFoundKeys = onFoundKeys;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._db.Execute(DoWork, _onError);
            }

            private void DoWork(NpgsqlConnection conn)
            {
                var list = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT document FROM " + _parent._partition + "_idx WHERE folder = :folder AND indexname = :indexname AND value >= :minvalue AND value <= :maxvalue";
                    cmd.Parameters.AddWithValue("folder", _folderName);
                    cmd.Parameters.AddWithValue("indexname", _indexName);
                    cmd.Parameters.AddWithValue("minvalue", _minValue);
                    cmd.Parameters.AddWithValue("maxvalue", _maxValue);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            list.Add(reader.GetString(0));
                    }
                }
                _parent._executor.Enqueue(new FindDocumentsCompleted(_onFoundKeys, list));
            }
        }
    }
}
