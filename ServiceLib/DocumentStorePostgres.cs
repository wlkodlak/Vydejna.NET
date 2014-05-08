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

            public Task DeleteAll()
            {
                return _parent._db.Execute(DeleteAllWorker, new DeleteAllParameters(_parent._partition, _folderName));
            }

            public Task<DocumentStoreFoundDocument> GetDocument(string name)
            {
                if (_parent.VerifyPath(name))
                {
                    return _parent._db.Query(GetDocumentWorker, new GetDocumentParameters(_parent._partition, _folderName, name, -1, false));
                }
                else
                    return TaskUtils.FromError<DocumentStoreFoundDocument>(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }

            public Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion)
            {
                if (_parent.VerifyPath(name))
                    return _parent._db.Query(GetDocumentWorker, new GetDocumentParameters(_parent._partition, _folderName, name, knownVersion, true));
                else
                    return TaskUtils.FromError<DocumentStoreFoundDocument>(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
            }


            public Task<bool> SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes)
            {
                if (!_parent.VerifyPath(name))
                    return TaskUtils.FromError<bool>(new ArgumentOutOfRangeException("name", string.Format("Name {0} is not valid", name)));
                if (indexes != null)
                {
                    foreach (var index in indexes)
                    {
                        if (!_parent.VerifyPath(index.IndexName))
                            return TaskUtils.FromError<bool>(new ArgumentOutOfRangeException("indexes", string.Format("Index name {0} is not valid", index.IndexName)));
                    }
                }
                return _parent._db.Query(SaveDocumentWorker, new SaveDocumentParameters(_parent._partition, _folderName, name, value, expectedVersion, indexes));
            }


            public Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue)
            {
                if (!_parent.VerifyPath(indexName))
                    return TaskUtils.FromError<IList<string>>(new ArgumentOutOfRangeException("name", string.Format("Index name {0} is not valid", indexName)));
                return _parent._db.Query(FindDocumentKeysWorker, new FindDocumentKeysParameters(_parent._partition, _folderName, indexName, minValue, maxValue));
            }

            public Task<DocumentStoreFoundDocuments> FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
            {
                if (!_parent.VerifyPath(indexName))
                    return TaskUtils.FromError<DocumentStoreFoundDocuments>(new ArgumentOutOfRangeException("name", string.Format("Index name {0} is not valid", indexName)));
                return _parent._db.Query(FindDocumentsWorker, new FindDocumentsParameters(_parent._partition, _folderName, indexName, minValue, maxValue, skip, maxCount, ascending));
            }
        }

        public DocumentStorePostgres(DatabasePostgres db, string partition)
        {
            _db = db;
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

        private class DeleteAllParameters
        {
            public readonly string Partition;
            public readonly string FolderName;

            public DeleteAllParameters(string partition, string folderName)
            {
                Partition = partition;
                FolderName = folderName;
            }
        }

        private static void DeleteAllWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (DeleteAllParameters)objContext;
            using (var tran = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "DELETE FROM ", context.Partition, " WHERE folder = '", context.FolderName, "'; ",
                        "DELETE FROM ", context.Partition, "_idx WHERE folder = '", context.FolderName, "'; ",
                        "NOTIFY ", context.Partition, ";");
                    cmd.ExecuteNonQuery();
                }
                tran.Commit();
            }
        }

        private class GetDocumentParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string DocumentName;
            public readonly int KnownVersion;
            public readonly bool CanOmitContents;

            public GetDocumentParameters(string partition, string folderName, string name, int knownVersion, bool canOmitContents)
            {
                Partition = partition;
                FolderName = folderName;
                DocumentName = name;
                KnownVersion = knownVersion;
                CanOmitContents = canOmitContents;
            }
        }

        private static DocumentStoreFoundDocument GetDocumentWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (GetDocumentParameters)objContext;
            if (context.CanOmitContents)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM " + context.Partition + " WHERE folder = :folder AND document = :document LIMIT 1";
                    cmd.Parameters.AddWithValue("folder", context.FolderName);
                    cmd.Parameters.AddWithValue("document", context.DocumentName);
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        int version;
                        if (!reader.Read())
                        {
                            return null;
                        }
                        else
                        {
                            version = reader.GetInt32(0);
                            if (version == context.KnownVersion)
                            {
                                return new DocumentStoreFoundDocument(context.DocumentName, version, false, null);
                            }
                        }
                    }
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version, contents FROM " + context.Partition + " WHERE folder = :folder AND document = :document LIMIT 1";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.DocumentName);
                using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                {
                    int version;
                    string contents;
                    if (reader.Read())
                    {
                        version = reader.GetInt32(0);
                        contents = reader.GetString(1);
                        return new DocumentStoreFoundDocument(context.DocumentName, version, true, contents);
                    }
                    else
                        return null;
                }
            }

        }

        private class SaveDocumentParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string Name;
            public readonly string Value;
            public readonly DocumentStoreVersion ExpectedVersion;
            public readonly IList<DocumentIndexing> Indexes;

            public SaveDocumentParameters(string partition, string folderName, string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes)
            {
                Partition = partition;
                FolderName = folderName;
                Name = name;
                Value = value;
                ExpectedVersion = expectedVersion;
                Indexes = indexes;
            }
        }

        private static bool SaveDocumentWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (SaveDocumentParameters)objContext;
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
                            cmd.CommandText = "SELECT version FROM " + context.Partition + " WHERE folder = :folder AND document = :document LIMIT 1 FOR UPDATE";
                            cmd.Parameters.AddWithValue("folder", context.FolderName);
                            cmd.Parameters.AddWithValue("document", context.Name);
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
                                cmd.CommandText = "INSERT INTO " + context.Partition + " (folder, document, version, contents) VALUES (:folder, :document, 1, :contents)";
                                cmd.Parameters.AddWithValue("folder", context.FolderName);
                                cmd.Parameters.AddWithValue("document", context.Name);
                                cmd.Parameters.AddWithValue("contents", context.Value);
                                cmd.ExecuteNonQuery();
                            }
                            if (context.Indexes != null)
                            {
                                var retryIndex = true;
                                var deleteBeforeInsert = false;
                                while (retryIndex)
                                {
                                    retryIndex = false;
                                    try
                                    {
                                        if (deleteBeforeInsert)
                                        {
                                            using (var cmd = conn.CreateCommand())
                                            {
                                                cmd.CommandText = "DELETE FROM " + context.Partition + "_idx WHERE folder = :folder AND document = :document";
                                                cmd.Parameters.AddWithValue("folder", context.FolderName);
                                                cmd.Parameters.AddWithValue("document", context.Name);
                                                cmd.ExecuteNonQuery();
                                            }
                                        }
                                        using (var cmd = conn.CreateCommand())
                                        {
                                            cmd.CommandText = "INSERT INTO " + context.Partition + "_idx (folder, document, indexname, value) SELECT :folder, :document, :indexname, unnest(:value)";
                                            cmd.Parameters.AddWithValue("folder", context.FolderName);
                                            cmd.Parameters.AddWithValue("document", context.Name);
                                            var paramIndex = cmd.Parameters.Add("indexname", NpgsqlTypes.NpgsqlDbType.Varchar);
                                            var paramValues = cmd.Parameters.Add("value", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                                            foreach (var indexChange in context.Indexes)
                                            {
                                                paramIndex.Value = indexChange.IndexName;
                                                paramValues.Value = indexChange.Values.Distinct().ToArray();
                                                cmd.ExecuteNonQuery();
                                            }
                                        }
                                    }
                                    catch (NpgsqlException ex)
                                    {
                                        if (ex.Code == "23505")
                                        {
                                            retryIndex = true;
                                            deleteBeforeInsert = true;
                                        }
                                        else
                                            throw;
                                    }
                                }
                            }
                            wasSaved = true;
                            tran.Commit();
                        }
                        else if (context.ExpectedVersion.VerifyVersion(documentVersion))
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "UPDATE " + context.Partition + " SET version = :version, contents = :contents WHERE folder = :folder AND document = :document";
                                cmd.Parameters.AddWithValue("folder", context.FolderName);
                                cmd.Parameters.AddWithValue("document", context.Name);
                                cmd.Parameters.AddWithValue("version", documentVersion + 1);
                                cmd.Parameters.AddWithValue("contents", context.Value);
                                cmd.ExecuteNonQuery();
                            }
                            if (context.Indexes != null)
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "DELETE FROM " + context.Partition + "_idx WHERE folder = :folder AND document = :document";
                                    cmd.Parameters.AddWithValue("folder", context.FolderName);
                                    cmd.Parameters.AddWithValue("document", context.Name);
                                    cmd.ExecuteNonQuery();
                                }
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "INSERT INTO " + context.Partition + "_idx (folder, document, indexname, value) SELECT :folder, :document, :indexname, unnest(:value)";
                                    cmd.Parameters.AddWithValue("folder", context.FolderName);
                                    cmd.Parameters.AddWithValue("document", context.Name);
                                    var paramIndex = cmd.Parameters.Add("indexname", NpgsqlTypes.NpgsqlDbType.Varchar);
                                    var paramValues = cmd.Parameters.Add("value", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                                    foreach (var indexChange in context.Indexes)
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

            return wasSaved;
        }

        private class FindDocumentKeysParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string IndexName;
            public readonly string MinValue;
            public readonly string MaxValue;

            public FindDocumentKeysParameters(string partition, string folderName, string indexName, string minValue, string maxValue)
            {
                Partition = partition;
                FolderName = folderName;
                IndexName = indexName;
                MinValue = minValue;
                MaxValue = maxValue;
            }
        }

        private static IList<string> FindDocumentKeysWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (FindDocumentKeysParameters)objContext;
            var list = new HashSet<string>();
            using (var cmd = conn.CreateCommand())
            {
                var cmdBuilder = new StringBuilder();
                cmdBuilder.Append("SELECT document FROM ").Append(context.Partition).Append("_idx WHERE folder = :folder AND indexname = :indexname");
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("indexname", context.IndexName);
                if (context.MinValue != null)
                {
                    cmdBuilder.Append(" AND value >= :minvalue");
                    cmd.Parameters.AddWithValue("minvalue", context.MinValue);
                }
                if (context.MaxValue != null)
                {
                    cmdBuilder.Append(" AND value <= :maxvalue");
                    cmd.Parameters.AddWithValue("maxvalue", context.MaxValue);
                }
                cmd.CommandText = cmdBuilder.ToString();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        list.Add(reader.GetString(0));
                }
            }
            return list.ToList();
        }

        private class FindDocumentsParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string IndexName;
            public readonly string MinValue;
            public readonly string MaxValue;
            public readonly int Skip;
            public readonly int MaxCount;
            public readonly bool Ascending;

            public FindDocumentsParameters(string partition, string folderName, string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
            {
                Partition = partition;
                FolderName = folderName;
                IndexName = indexName;
                MinValue = minValue;
                MaxValue = maxValue;
                Skip = skip;
                MaxCount = maxCount;
                Ascending = ascending;
            }
        }

        private static DocumentStoreFoundDocuments FindDocumentsWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (FindDocumentsParameters)objContext;
            bool needsSeparateTotalCount = false;
            var list = new DocumentStoreFoundDocuments();
            using (var cmd = conn.CreateCommand())
            {
                var cmdBuilder = new StringBuilder(300);
                cmdBuilder
                    .Append("SELECT d.document, d.version, d.contents FROM ")
                    .Append(context.Partition)
                    .Append("_idx i JOIN ")
                    .Append(context.Partition)
                    .Append(" d ON i.document = d.document AND i.folder = d.folder")
                    .Append(" WHERE i.folder = :folder AND i.indexname = :indexname");
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("indexname", context.IndexName);
                if (context.MinValue != null)
                {
                    cmdBuilder.Append(" AND value >= :minvalue");
                    cmd.Parameters.AddWithValue("minvalue", context.MinValue);
                }
                if (context.MaxValue != null)
                {
                    cmdBuilder.Append(" AND value <= :maxvalue");
                    cmd.Parameters.AddWithValue("maxvalue", context.MaxValue);
                }
                cmdBuilder.Append(" ORDER BY i.value");
                if (!context.Ascending)
                {
                    cmdBuilder.Append(" DESC");
                }
                if (context.Skip > 0)
                {
                    cmdBuilder.Append(" OFFSET ").Append(context.Skip);
                    needsSeparateTotalCount = true;
                }
                if (context.MaxCount < int.MaxValue)
                {
                    cmdBuilder.Append(" LIMIT ").Append(context.MaxCount);
                    needsSeparateTotalCount = true;
                }
                cmd.CommandText = cmdBuilder.ToString();
                using (var reader = cmd.ExecuteReader())
                {
                    var allKeys = new HashSet<string>();
                    while (reader.Read())
                    {
                        var documentName = reader.GetString(0);
                        if (!allKeys.Add(documentName))
                            continue;
                        var documentVersion = reader.GetInt32(1);
                        var documentData = reader.GetString(2);
                        list.Add(new DocumentStoreFoundDocument(documentName, documentVersion, true, documentData));
                    }
                    list.TotalFound = allKeys.Count;
                }
            }
            if (needsSeparateTotalCount)
            {
                using (var cmd = conn.CreateCommand())
                {
                    var cmdBuilder = new StringBuilder(300);
                    cmdBuilder
                        .Append("SELECT COUNT(DISTINCT i.document)::integer FROM ")
                        .Append(context.Partition)
                        .Append("_idx i WHERE i.folder = :folder AND i.indexname = :indexname");
                    cmd.Parameters.AddWithValue("folder", context.FolderName);
                    cmd.Parameters.AddWithValue("indexname", context.IndexName);
                    if (context.MinValue != null)
                    {
                        cmdBuilder.Append(" AND value >= :minvalue");
                        cmd.Parameters.AddWithValue("minvalue", context.MinValue);
                    }
                    if (context.MaxValue != null)
                    {
                        cmdBuilder.Append(" AND value <= :maxvalue");
                        cmd.Parameters.AddWithValue("maxvalue", context.MaxValue);
                    }
                    cmd.CommandText = cmdBuilder.ToString();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            list.TotalFound = reader.GetInt32(0);
                        }
                    }
                }
            }
            return list;
        }
    }
}
