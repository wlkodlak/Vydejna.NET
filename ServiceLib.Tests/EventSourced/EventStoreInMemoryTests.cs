using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using System.Collections.Generic;
using Npgsql;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStoreInMemoryTests : EventStoreTestBase
    {
        protected override IEventStoreWaitable GetEventStore()
        {
            return new EventStoreInMemory(Executor);
        }
    }

    [TestClass]
    public class EventStorePostgresTests : EventStoreTestBase
    {
        private DatabasePostgres _db;
        private List<IDisposable> _disposables;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _disposables = new List<IDisposable>();
            _db = new DatabasePostgres(GetConnectionString(), Executor);
        }

        private string GetConnectionString()
        {
            var connString = new NpgsqlConnectionStringBuilder();
            connString.Database = "servicelibtest";
            connString.Host = "localhost";
            connString.UserName = "postgres";
            connString.Password = "postgres";
            return connString.ToString();
        }

        protected override IEventStoreWaitable GetEventStore()
        {
            var store = new EventStorePostgres(_db, Executor);
            _disposables.Add(store);
            store.Initialize();
            _db.ExecuteSync(DeleteAllEvents);
            return store;
        }

        private void DeleteAllEvents(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM eventstore_streams; DELETE FROM eventstore_events; SELECT setval('eventstore_events_id_seq', 1);";
                cmd.ExecuteNonQuery();
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
        }
    }
}