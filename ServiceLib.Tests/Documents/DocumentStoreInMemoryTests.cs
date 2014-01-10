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
        protected override IDocumentStore CreateEmptyDocumentStore(IQueueExecution executor)
        {
            return new DocumentStoreInMemory(executor);
        }
    }

    [TestClass]
    public class DocumentStorePostgresTests : DocumentStoreTestBase
    {
        private List<IDisposable> _disposables = new List<IDisposable>();

        private string GetConnectionString()
        {
            var connString = new NpgsqlConnectionStringBuilder();
            connString.Database = "servicelibtest";
            connString.Host = "localhost";
            connString.UserName = "postgres";
            connString.Password = "postgres";
            return connString.ToString();
        }
        protected override IDocumentStore CreateEmptyDocumentStore(IQueueExecution executor)
        {
            var db = new DatabasePostgres(GetConnectionString(), executor);
            var store = new DocumentStorePostgres(db, executor);
            _disposables.Add(store);
            store.Initialize();
            db.ExecuteSync(DeleteAllDocuments);
            return store;
        }

        private void DeleteAllDocuments(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM documents";
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
