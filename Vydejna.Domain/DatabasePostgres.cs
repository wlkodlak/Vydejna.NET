using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;

namespace Vydejna.Domain
{
    public class DatabasePostgres
    {
        private string _connectionString;

        public DatabasePostgres(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void OpenConnection(Action<NpgsqlConnection> onConnected, Action<Exception> onError)
        {
            new OpenConnectionWorker(_connectionString, onConnected, onError).Execute();
        }

        public void Execute(Action<NpgsqlConnection> handler, Action<Exception> onError)
        {
            new ExecuteWorker(_connectionString, handler, onError).Execute();
        }

        public void ReleaseConnection(NpgsqlConnection conn)
        {
            conn.Close();
        }

        private class OpenConnectionWorker
        {
            private readonly string _connectionString;
            private readonly Action<NpgsqlConnection> _onConnected;
            private readonly Action<Exception> _onError;
            private readonly NpgsqlConnection _conn;

            public OpenConnectionWorker(string connectionString, Action<NpgsqlConnection> onConnected, Action<Exception> onError)
            {
                _connectionString = connectionString;
                _onConnected = onConnected;
                _onError = onError;
                _conn = new NpgsqlConnection();
            }

            public void Execute()
            {
                _conn.ConnectionString = _connectionString;
                _conn.OpenAsync().ContinueWith(OpenCompleted);
            }
            private void OpenCompleted(Task task)
            {
                if (task.Exception != null)
                    _onError(task.Exception.GetBaseException());
                else
                    _onConnected(_conn);
            }
        }

        private class ExecuteWorker
        {
            private string _connectionString;
            private Action<NpgsqlConnection> _handler;
            private Action<Exception> _onError;

            public ExecuteWorker(string connectionString, Action<NpgsqlConnection> handler, Action<Exception> onError)
            {
                _connectionString = connectionString;
                _handler = handler;
                _onError = onError;
            }

            public void Execute()
            {
                Task.Factory.StartNew(TaskFunc);
            }

            private void TaskFunc()
            {
                try
                {
                    using (var conn = new NpgsqlConnection())
                    {
                        conn.ConnectionString = _connectionString;
                        conn.Open();
                        _handler(conn);
                    }
                }
                catch (Exception exception)
                {
                    _onError(exception);
                }
            }
        }

        public void ExecuteSync(Action<NpgsqlConnection> handler)
        {
            using (var conn = new NpgsqlConnection())
            {
                conn.ConnectionString = _connectionString;
                conn.Open();
                handler(conn);
            }
        }
    }
}
