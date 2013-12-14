using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IDocumentStore
    {
        Task<string> GetDocument(string key);
        Task SaveDocument(string key, string value);
    }

    public class DocumentStoreInMemory : IDocumentStore
    {
        private ConcurrentDictionary<string, string> _data;

        public DocumentStoreInMemory()
        {
            _data = new ConcurrentDictionary<string, string>();
        }

        public Task<string> GetDocument(string key)
        {
            string value;
            if (_data.TryGetValue(key, out value))
                return TaskResult.GetCompletedTask(value);
            else
                return TaskResult.GetCompletedTask("");
        }

        public Task SaveDocument(string key, string value)
        {
            _data[key] = value;
            return TaskResult.GetCompletedTask();
        }
    }

    public class DocumentStoreSql : IDocumentStore
    {
        private ISqlConfiguration _config;

        public DocumentStoreSql(ISqlConfiguration config)
        {
            _config = config;
        }

        public async Task CreateTables()
        {
            var conn = await _config.GetConnection().ConfigureAwait(false);
            try
            {
                var existingTables = new HashSet<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sys.objects WHERE type = 'U'";
                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (reader.Read())
                            existingTables.Add(reader.GetString(0));
                    }
                }

                if (!existingTables.Contains("documents"))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "CREATE TABLE [dbo].[documents] (" +
                            "[id] [varchar](64) NOT NULL," +
                            "[body] [text] NULL," +
                            "CONSTRAINT [PK_documents] PRIMARY KEY CLUSTERED ([id] ASC)" +
                            ")";
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        public async Task<string> GetDocument(string key)
        {
            var conn = await _config.GetConnection().ConfigureAwait(false);
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT body FROM documents WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", key);
                    return (string)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        public async Task SaveDocument(string key, string value)
        {
            var conn = await _config.GetConnection().ConfigureAwait(false);
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "MERGE INTO documents AS target " +
                        "USING (SELECT @id, @body) AS source (id, body) " +
                        "ON target.id = source.id " +
                        "WHEN MATCHED THEN UPDATE SET body = source.body " +
                        "WHEN NOT MATCHED THEN INSERT (id, body) VALUES (source.id, source.body);";
                    cmd.Parameters.AddWithValue("@id", key);
                    cmd.Parameters.AddWithValue("@body", value);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }
    }
}
