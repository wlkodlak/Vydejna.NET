using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class DocumentStoreInMemoryTests : DocumentStoreTestBase
    {
        private Dictionary<string, DocumentStoreInMemory> Partitions;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            Partitions = new Dictionary<string, DocumentStoreInMemory>();
        }

        protected override IDocumentStore CreateEmptyDocumentStore()
        {
            return Partitions[CurrentPartition] = new DocumentStoreInMemory();
        }

        protected override IDocumentStore GetExistingDocumentStore()
        {
            return Partitions[CurrentPartition];
        }
    }

    [TestClass]
    public class DocumentStorePostgresTests : DocumentStoreTestBase
    {
        private DatabasePostgres _db;
        private List<IDisposable> _disposables;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _disposables = new List<IDisposable>();
            _db = new DatabasePostgres(GetConnectionString());
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
        protected override IDocumentStore CreateEmptyDocumentStore()
        {
            var store = new DocumentStorePostgres(_db, CurrentPartition);
            _disposables.Add(store);
            store.Initialize();
            _db.ExecuteSync(DeleteAllDocuments);
            return store;
        }
        protected override IDocumentStore GetExistingDocumentStore()
        {
            var store = new DocumentStorePostgres(_db, CurrentPartition);
            _disposables.Add(store);
            return store;
        }

        private void DeleteAllDocuments(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM " + CurrentPartition;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM " + CurrentPartition + "_idx";
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
