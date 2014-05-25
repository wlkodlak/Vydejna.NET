using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using System.Collections.Generic;
using Npgsql;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStoreInMemoryTests : EventStoreTestBase
    {
        protected override IEventStoreWaitable GetEventStore()
        {
            return new EventStoreInMemory();
        }
    }

    [TestClass]
    public class EventStoreInTestStoreTests : EventStoreTestBase
    {
        protected override IEventStoreWaitable GetEventStore()
        {
            return new TestEventStore();
        }
    }

    [TestClass]
    public class EventStorePostgresTests : EventStoreTestBase
    {
        private DatabasePostgres _db;
        private List<IDisposable> _disposables;
        private VirtualTime _time;
        private Timer _timer;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            Scheduler.AllowWaiting(2, 75);
            _time = new VirtualTime();
            _disposables = new List<IDisposable>();
            _db = new DatabasePostgres(GetConnectionString(), _time);
            _db.ExecuteSync(Drop);
            _timer = new Timer(o => _time.SetTime(_time.GetUtcTime().AddSeconds(5)), null, 500, 500);
            _disposables.Add(_db);
            _disposables.Add(_timer);
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
            var store = new EventStorePostgres(_db, _time);
            _disposables.Add(store);
            store.Initialize();
            return store;
        }

        private void Drop(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE IF EXISTS eventstore_streams; DROP TABLE IF EXISTS eventstore_events; DROP TABLE IF EXISTS eventstore_snapshots;";
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