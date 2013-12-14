using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;

namespace Vydejna.Domain
{
    public interface ISqlConfiguration
    {
        Task<SqlConnection> GetConnection();
        void ReleaseConnection(SqlConnection conn);
    }

    public class SqlConfiguration : ISqlConfiguration
    {
        private string _connString;

        public SqlConfiguration(string connString)
        {
            _connString = connString;
        }

        public async Task<SqlConnection> GetConnection()
        {
            var conn = new SqlConnection();
            conn.ConnectionString = _connString;
            await conn.OpenAsync();
            return conn;
        }

        public void ReleaseConnection(SqlConnection conn)
        {
            conn.Dispose();
        }
    }

    public class EventStoreSql : IEventStore
    {
        private ISqlConfiguration _config;

        public EventStoreSql(ISqlConfiguration config)
        {
            _config = config;
        }

        public async Task CreateTables()
        {
            var conn = await _config.GetConnection();
            try
            {
                var existingTables = new HashSet<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sys.objects WHERE type = 'U'";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                            existingTables.Add(reader.GetString(0));
                    }
                }

                if (!existingTables.Contains("eventstore_streams"))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "CREATE TABLE [dbo].[eventstore_streams] (" +
                            "[stream] [char](32) NOT NULL, " +
                            "[version] [int] NOT NULL, " +
                            "CONSTRAINT [IX_eventstore_streams] UNIQUE NONCLUSTERED ([stream] ASC)" +
                            ")";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (!existingTables.Contains("eventstore_events"))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "CREATE TABLE [dbo].[eventstore_events](" +
                            "[id] [int] IDENTITY(1,1) NOT NULL," +
                            "[stream] [char](32) NOT NULL," +
                            "[type] [varchar](64) NOT NULL," +
                            "[version] [int] NOT NULL," +
                            "[body] [text] NULL," +
                            "CONSTRAINT [PK_eventstore_events] PRIMARY KEY CLUSTERED ([id] ASC)," +
                            "CONSTRAINT [IX_eventstore_events] UNIQUE NONCLUSTERED ([stream] ASC, [version] ASC)" +
                            ")";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        public async Task AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            var conn = await _config.GetConnection();
            var eventsList = events.ToList();
            try
            {
                using (var tran = conn.BeginTransaction())
                {
                    int originalVersion = await GetStreamVersion(conn, stream, true);
                    expectedVersion.Verify(originalVersion);
                    int lastVersion = originalVersion;
                    foreach (var item in eventsList)
                    {
                        item.StreamName = stream;
                        item.StreamVersion = ++lastVersion;
                    }
                    await InsertEvents(conn, eventsList);
                    await UpdateStreamVersion(conn, stream, lastVersion);
                    var tokens = await GetTokens(conn, stream, originalVersion + 1, lastVersion);
                    foreach (var item in eventsList)
                    {
                        int index = item.StreamVersion - originalVersion - 1;
                        item.Token = new EventStoreToken(tokens[index].ToString());
                    }
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        private async Task<List<Tuple<int, int>>> GetTokens(SqlConnection conn, string stream, int minVersion, int maxVersion)
        {
            using (var cmd = conn.CreateCommand())
            {
                var results = new List<Tuple<int, int>>();
                cmd.CommandText =
                    "SELECT version, id FROM eventstore_events " +
                    "WHERE stream = @stream AND version >= @minversion AND version <= @maxversion";
                cmd.Parameters.AddWithValue("@stream", stream);
                cmd.Parameters.AddWithValue("@minversion", minVersion);
                cmd.Parameters.AddWithValue("@maxversion", maxVersion);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        results.Add(new Tuple<int, int>(reader.GetInt32(0), reader.GetInt32(1)));
                }
                return results;
            }
        }

        private async Task UpdateStreamVersion(SqlConnection conn, string stream, int lastVersion)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE eventstore_streams SET version = @version WHERE stream = @stream";
                cmd.Parameters.AddWithValue("@stream", stream);
                cmd.Parameters.AddWithValue("@version", lastVersion);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task InsertEvents(SqlConnection conn, IList<EventStoreEvent> items)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO eventstore_events (stream, version, type, body) " +
                    "VALUES (@stream, @version, @type, @body)";
                var paramStream = cmd.Parameters.Add("@stream", System.Data.SqlDbType.Char, 32);
                var paramVersion = cmd.Parameters.Add("@version", System.Data.SqlDbType.Int);
                var paramType = cmd.Parameters.Add("@type", System.Data.SqlDbType.VarChar, 64);
                var paramBody = cmd.Parameters.Add("@body", System.Data.SqlDbType.Text);
                cmd.Prepare();

                foreach (var item in items)
                {
                    paramStream.Value = item.StreamName;
                    paramVersion.Value = item.StreamVersion;
                    paramType.Value = item.Type;
                    paramBody.Value = item.Body;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<int> GetStreamVersion(SqlConnection conn, string stream, bool updateLock)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT version FROM eventstore_streams " +
                    (updateLock ? "(UPDLOCK) " : "") +
                    "WHERE stream = @stream";
                cmd.Parameters.AddWithValue("@stream", stream);
                return (int)await cmd.ExecuteScalarAsync();
            }
        }

        public async Task<IEventStoreStream> ReadStream(string stream, int minVersion = 0, int maxCount = int.MaxValue, bool loadBody = true)
        {
            var conn = await _config.GetConnection();
            try
            {
                int currentVersion = await GetStreamVersion(conn, stream, false);
                var events = await LoadStreamEvents(conn, stream, minVersion, maxCount);
                return new EventStoreStream(events, currentVersion, 0);
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        private async Task<List<EventStoreEvent>> LoadStreamEvents(SqlConnection conn, string stream, int minVersion, int maxCount)
        {
            using (var cmd = conn.CreateCommand())
            {
                var results = new List<EventStoreEvent>();
                cmd.CommandText =
                    "SELECT TOP (@maxcount) id, version, type, body FROM eventstore_events " +
                    "WHERE stream = @stream AND version >= @minversion ORDER BY version";
                cmd.Parameters.AddWithValue("@stream", stream);
                cmd.Parameters.AddWithValue("@minversion", minVersion);
                cmd.Parameters.AddWithValue("@maxcount", maxCount);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var stored = new EventStoreEvent();
                        stored.Token = new EventStoreToken(reader.GetInt32(0).ToString());
                        stored.StreamName = stream;
                        stored.StreamVersion = reader.GetInt32(1);
                        stored.Type = reader.GetString(2);
                        stored.Body = reader.GetString(3);
                        results.Add(stored);
                    }
                }
                return results;
            }
        }

        public async Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount = int.MaxValue, bool loadBody = false)
        {
            var conn = await _config.GetConnection();
            try
            {
                if (token.IsCurrent)
                    return new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), await GetCurrentToken(conn), false);
                var id = token.IsInitial ? 0 : int.Parse(token.ToString());
                if (maxCount < int.MaxValue)
                    maxCount++;
                var events = await LoadAllEvents(conn, id, maxCount, loadBody);
                if (events.Count == 0)
                    return new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), token, false);
                else if (maxCount == 1)
                    return new EventStoreCollection(Enumerable.Empty<EventStoreEvent>(), token, true);
                else
                    return new EventStoreCollection(events.Take(maxCount - 1), events.Last().Token, events.Count == maxCount);
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }
        }

        private async Task<EventStoreToken> GetCurrentToken(SqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                var results = new List<EventStoreEvent>();
                cmd.CommandText = "SELECT TOP 1 id FROM eventstore_events ORDER BY id DESC";
                using (var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow))
                {
                    if (!reader.Read())
                        return EventStoreToken.Initial;
                    else if (reader.IsDBNull(0))
                        return EventStoreToken.Initial;
                    else
                        return new EventStoreToken(reader.GetInt32(0).ToString());
                }
            }
        }

        private async Task<List<EventStoreEvent>> LoadAllEvents(SqlConnection conn, int id, int maxCount, bool loadBody)
        {
            using (var cmd = conn.CreateCommand())
            {
                var results = new List<EventStoreEvent>();
                cmd.CommandText = 
                    "SELECT TOP (@maxcount) id, stream, version, type" +
                    (loadBody ? ", body " : "") +
                    " FROM eventstore_events WHERE id > @id ORDER BY id";
                cmd.Parameters.AddWithValue("@maxcount", maxCount);
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var stored = new EventStoreEvent();
                        stored.Token = new EventStoreToken(reader.GetInt32(0).ToString());
                        stored.StreamName = reader.GetString(1);
                        stored.StreamVersion = reader.GetInt32(2);
                        stored.Type = reader.GetString(3);
                        if (loadBody)
                            stored.Body = reader.GetString(4);
                        results.Add(stored);
                    }
                }
                return results;
            }
        }

        public async Task LoadBodies(IList<EventStoreEvent> events)
        {
            var withoutBodies = events.Where(e => e.Body == null && e.Token != null).ToList();
            if (withoutBodies.Count == 0)
                return;
            var ids = withoutBodies
                .Select(evt => new { id = int.Parse(evt.Token.ToString()), evt })
                .ToDictionary(c => c.id);

            var conn = await _config.GetConnection();
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    var sb = new StringBuilder();
                    sb.Append("SELECT id, body FROM eventstore_events WHERE id IN (");
                    sb.Append(ids[0]);
                    for (int i = 1; i < ids.Count; i++)
                        sb.Append(", ").Append(ids[i]);
                    sb.Append(")");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ids[reader.GetInt32(0)].evt.Body = reader.GetString(1);
                    }
                }
            }
            finally
            {
                _config.ReleaseConnection(conn);
            }

        }
    }
}
