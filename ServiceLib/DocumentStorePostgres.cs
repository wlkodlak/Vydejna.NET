using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace ServiceLib
{
    public class DocumentStorePostgres : IDocumentStore, IDisposable
    {
        private readonly DatabasePostgres _db;
        private readonly Regex _pathRegex;
        private readonly string _partition;

        private static readonly DocumentStorePostgresTraceSource Logger =
            new DocumentStorePostgresTraceSource("ServiceLib.DocumentStore");

        private class Folder : IDocumentFolder
        {
            private readonly DocumentStorePostgres _parent;
            private readonly string _folderName;

            public Folder(DocumentStorePostgres parent, string folderName)
            {
                _parent = parent;
                _folderName = folderName;
            }

            public async Task DeleteAll()
            {
                var parameters = new DeleteAllParameters(_parent._partition, _folderName);
                await _parent._db.Execute(DeleteAllWorker, parameters);
            }

            private void AssertPathOk(string documentName)
            {
                if (!_parent.VerifyPath(documentName))
                    throw new DocumentNameInvalidException(documentName);
            }

            private void AssertIndexesOk(IList<DocumentIndexing> indexes)
            {
                if (indexes == null) return;
                foreach (var index in indexes)
                {
                    AssertPathOk(index.IndexName);
                }
            }

            public async Task<DocumentStoreFoundDocument> GetDocument(string name)
            {
                AssertPathOk(name);
                var parameters = new GetDocumentParameters(_parent._partition, _folderName, name, -1, false);
                return await _parent._db.Query(GetDocumentWorker, parameters);
            }

            public async Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion)
            {
                AssertPathOk(name);
                var parameters = new GetDocumentParameters(_parent._partition, _folderName, name, knownVersion, true);
                return await _parent._db.Query(GetDocumentWorker, parameters);
            }


            public async Task<bool> SaveDocument(
                string name, string value,
                DocumentStoreVersion expectedVersion,
                IList<DocumentIndexing> indexes)
            {
                AssertPathOk(name);
                AssertIndexesOk(indexes);
                var parameters = new SaveDocumentParameters(
                    _parent._partition, _folderName, name, value, expectedVersion, indexes);
                return await _parent._db.Query(SaveDocumentWorker, parameters);
            }


            public async Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue)
            {
                AssertPathOk(indexName);
                var parameters = new FindDocumentKeysParameters(
                    _parent._partition, _folderName, indexName, minValue, maxValue);
                return await _parent._db.Query(FindDocumentKeysWorker, parameters);
            }

            public async Task<DocumentStoreFoundDocuments> FindDocuments(
                string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
            {
                AssertPathOk(indexName);
                var parameters = new FindDocumentsParameters(
                    _parent._partition, _folderName, indexName, minValue, maxValue, skip, maxCount, ascending);
                return await _parent._db.Query(FindDocumentsWorker, parameters);
            }
        }

        public DocumentStorePostgres(DatabasePostgres db, string partition)
        {
            _db = db;
            _pathRegex = new Regex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);
            _partition = partition;
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
            using (new LogMethod(Logger, "InitializeDatabase"))
            {
                var tables = new HashSet<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT relname FROM pg_catalog.pg_class c " +
                        "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
                        "WHERE c.relkind = 'r' AND n.nspname = 'public'";
                    Logger.TraceSql(cmd);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            tables.Add(reader.GetString(0));
                    }
                }
                var createdTables = new List<string>();
                if (!tables.Contains(_partition))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Concat(
                            "CREATE TABLE IF NOT EXISTS ", _partition,
                            " (folder varchar NOT NULL, document varchar NOT NULL, version integer NOT NULL, " +
                            "contents text NOT NULL, PRIMARY KEY (folder, document))");
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    createdTables.Add(_partition);
                }
                if (!tables.Contains(_partition + "_idx"))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Concat(
                            "CREATE TABLE IF NOT EXISTS ", _partition,
                            "_idx (folder varchar NOT NULL, indexname varchar NOT NULL, value varchar NOT NULL, " +
                            "document varchar NOT NULL, PRIMARY KEY (folder, indexname, value, document))");
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Concat(
                            "CREATE INDEX ON ", _partition, "_idx (folder, document, indexname)");
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    createdTables.Add(_partition + "_idx");
                }
                Logger.CreatedStorage(_partition, createdTables);
            }
        }

        private bool VerifyPath(string path)
        {
            return _pathRegex.IsMatch(path);
        }

        public IDocumentFolder GetFolder(string name)
        {
            if (!VerifyPath(name))
                throw new DocumentNameInvalidException(name);
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
            using (new LogMethod(Logger, "DeleteAll"))
            {
                var context = (DeleteAllParameters) objContext;
                using (var tran = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Concat(
                            "DELETE FROM ", context.Partition, " WHERE folder = '", context.FolderName, "'; ",
                            "DELETE FROM ", context.Partition, "_idx WHERE folder = '", context.FolderName, "'; ",
                            "NOTIFY ", context.Partition, ";");
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    tran.Commit();
                }
                Logger.DeletedFolder(context.FolderName);
            }
        }

        private class GetDocumentParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string DocumentName;
            public readonly int KnownVersion;
            public readonly bool CanOmitContents;

            public GetDocumentParameters(
                string partition,
                string folderName,
                string name,
                int knownVersion,
                bool canOmitContents)
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
            using (new LogMethod(Logger, "GetDocument"))
            {
                var context = (GetDocumentParameters) objContext;
                if (context.CanOmitContents)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT version FROM " + context.Partition +
                                          " WHERE folder = :folder AND document = :document LIMIT 1";
                        cmd.Parameters.AddWithValue("folder", context.FolderName);
                        cmd.Parameters.AddWithValue("document", context.DocumentName);
                        Logger.TraceSql(cmd);
                        using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            if (!reader.Read())
                            {
                                Logger.DocumentNotFound(context.FolderName, context.DocumentName);
                                return null;
                            }
                            else
                            {
                                var version = reader.GetInt32(0);
                                if (version == context.KnownVersion)
                                {
                                    Logger.DocumentUpToDate(
                                        context.FolderName, context.DocumentName, context.KnownVersion);
                                    return new DocumentStoreFoundDocument(context.DocumentName, version, false, null);
                                }
                            }
                        }
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version, contents FROM " + context.Partition +
                                      " WHERE folder = :folder AND document = :document LIMIT 1";
                    cmd.Parameters.AddWithValue("folder", context.FolderName);
                    cmd.Parameters.AddWithValue("document", context.DocumentName);
                    Logger.TraceSql(cmd);
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (reader.Read())
                        {
                            var version = reader.GetInt32(0);
                            var contents = reader.GetString(1);
                            Logger.DocumentRetrieved(context.FolderName, context.DocumentName, version, contents);
                            return new DocumentStoreFoundDocument(context.DocumentName, version, true, contents);
                        }
                        else
                        {
                            Logger.DocumentNotFound(context.FolderName, context.DocumentName);
                            return null;
                        }
                    }
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
            public int DocumentVersion;
            public bool WasSaved;

            public SaveDocumentParameters(
                string partition, string folderName, string name, string value,
                DocumentStoreVersion expectedVersion,
                IList<DocumentIndexing> indexes)
            {
                Partition = partition;
                FolderName = folderName;
                Name = name;
                Value = value;
                ExpectedVersion = expectedVersion;
                Indexes = indexes;
                DocumentVersion = -1;
                WasSaved = false;
            }
        }

        private static bool SaveDocumentWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "SaveDocument"))
            {
                var context = (SaveDocumentParameters) objContext;
                while (true)
                {
                    if (TrySaveDocument(conn, context))
                        return context.WasSaved;
                }
            }
        }

        private static bool TrySaveDocument(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            try
            {
                using (var tran = conn.BeginTransaction())
                {
                    context.DocumentVersion = GetDocumentVersionForSave(conn, context);

                    if (context.DocumentVersion == -1)
                    {
                        InsertNewDocument(conn, context);
                        if (context.Indexes != null)
                        {
                            UpdateIndexes(conn, context, tran, false);
                        }
                        context.WasSaved = true;
                        tran.Commit();
                        Logger.DocumentSaved(context.FolderName, context.Name, 1, context.Value);
                    }
                    else if (context.ExpectedVersion.VerifyVersion(context.DocumentVersion))
                    {
                        UpdateDocument(conn, context);
                        if (context.Indexes != null)
                        {
                            UpdateIndexes(conn, context, tran, true);
                        }
                        context.WasSaved = true;
                        tran.Commit();
                        Logger.DocumentSaved(
                            context.FolderName, context.Name, context.DocumentVersion + 1, context.Value);
                    }
                    else
                    {
                        context.WasSaved = false;
                        Logger.DocumentConflicted(
                            context.FolderName, context.Name, context.ExpectedVersion, context.DocumentVersion);
                    }
                }
                return true;
            }
            catch (NpgsqlException ex)
            {
                if (ex.Code == "23505")
                {
                    Logger.SaveDocumentWillRetry(context.FolderName, context.Name, ex);
                    return false;
                }
                else
                    throw;
            }
        }

        private static int GetDocumentVersionForSave(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version FROM " + context.Partition +
                                  " WHERE folder = :folder AND document = :document LIMIT 1 FOR UPDATE";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.Name);
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return reader.GetInt32(0);
                    else
                        return -1;
                }
            }
        }

        private static void InsertNewDocument(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO " + context.Partition +
                                  " (folder, document, version, contents) VALUES (:folder, :document, 1, :contents)";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.Name);
                cmd.Parameters.AddWithValue("contents", context.Value);
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpdateDocument(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE " + context.Partition +
                                  " SET version = :version, contents = :contents WHERE folder = :folder AND document = :document";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.Name);
                cmd.Parameters.AddWithValue("version", context.DocumentVersion + 1);
                cmd.Parameters.AddWithValue("contents", context.Value);
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpdateIndexes(
            NpgsqlConnection conn, SaveDocumentParameters context, NpgsqlTransaction tran, bool deleteBeforeInsert)
        {
            var retryIndex = true;
            while (retryIndex)
            {
                tran.Save("saveindex");
                retryIndex = false;
                try
                {
                    if (deleteBeforeInsert)
                    {
                        DeleteIndexContents(conn, context);
                    }
                    InsertIndexContents(conn, context);
                }
                catch (NpgsqlException ex)
                {
                    if (ex.Code == "23505")
                    {
                        retryIndex = true;
                        deleteBeforeInsert = true;
                        tran.Rollback("saveindex");
                        Logger.UpdateIndexWillRetry(context.FolderName, context.Name, ex);
                    }
                    else
                        throw;
                }
            }
        }

        private static void DeleteIndexContents(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM " + context.Partition +
                                  "_idx WHERE folder = :folder AND document = :document";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.Name);
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private static void InsertIndexContents(NpgsqlConnection conn, SaveDocumentParameters context)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO " + context.Partition +
                                  "_idx (folder, document, indexname, value) SELECT :folder, :document, :indexname, unnest(:value)";
                cmd.Parameters.AddWithValue("folder", context.FolderName);
                cmd.Parameters.AddWithValue("document", context.Name);
                var paramIndex = cmd.Parameters.Add("indexname", NpgsqlDbType.Varchar);
                // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                var paramValues = cmd.Parameters.Add("value", NpgsqlDbType.Array | NpgsqlDbType.Varchar);
                foreach (var indexChange in context.Indexes)
                {
                    paramIndex.Value = indexChange.IndexName;
                    var valuesArray = indexChange.Values.Distinct().ToArray();
                    paramValues.Value = valuesArray;
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private class FindDocumentKeysParameters
        {
            public readonly string Partition;
            public readonly string FolderName;
            public readonly string IndexName;
            public readonly string MinValue;
            public readonly string MaxValue;

            public FindDocumentKeysParameters(string partition,string folderName,string indexName,string minValue,string maxValue)
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
            using (new LogMethod(Logger, "FindDocumentKeys"))
            {
                var context = (FindDocumentKeysParameters) objContext;
                var list = new HashSet<string>();
                using (var cmd = conn.CreateCommand())
                {
                    var cmdBuilder = new StringBuilder();
                    cmdBuilder.Append("SELECT document FROM ")
                        .Append(context.Partition)
                        .Append("_idx WHERE folder = :folder AND indexname = :indexname");
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
                    Logger.TraceSql(cmd);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            list.Add(reader.GetString(0));
                    }
                    Logger.FoundDocumentKeys(
                        context.FolderName, context.IndexName, context.MinValue, context.MaxValue, list);
                }
                return list.ToList();
            }
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
            
            public bool NeedsSeparateTotalCount;

            public FindDocumentsParameters(
                string partition, string folderName, string indexName,
                string minValue, string maxValue,
                int skip, int maxCount, bool ascending)
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
            using (new LogMethod(Logger, "FindDocuments"))
            {
                var context = (FindDocumentsParameters) objContext;
                var list = new DocumentStoreFoundDocuments();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = BuildFindDocumentsCommand(context, cmd);
                    Logger.TraceSql(cmd);
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
                if (context.NeedsSeparateTotalCount)
                {
                    CalculateTotalFoundDocuments(conn, context, list);
                }

                Logger.FoundDocuments(
                    context.FolderName, context.IndexName, context.MinValue, context.MaxValue,
                    context.Skip, context.MaxCount, context.Ascending, list);
                return list;
            }
        }

        private static string BuildFindDocumentsCommand(FindDocumentsParameters context, NpgsqlCommand cmd)
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
                context.NeedsSeparateTotalCount = true;
            }
            if (context.MaxCount < int.MaxValue)
            {
                cmdBuilder.Append(" LIMIT ").Append(context.MaxCount);
                context.NeedsSeparateTotalCount = true;
            }
            var commandText = cmdBuilder.ToString();
            return commandText;
        }

        private static void CalculateTotalFoundDocuments(
            NpgsqlConnection conn, FindDocumentsParameters context, DocumentStoreFoundDocuments list)
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
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        list.TotalFound = reader.GetInt32(0);
                    }
                }
            }
        }
    }
}
