using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;

namespace ServiceLib
{
    public class DatabasePostgres
    {
        private string _connectionString;
        private IQueueExecution _executor;

        public DatabasePostgres(string connectionString, IQueueExecution executor)
        {
            _connectionString = connectionString;
            _executor = executor;
        }

        public void OpenConnection(Action<NpgsqlConnection> onConnected, Action<Exception> onError)
        {
            new OpenConnectionWorker(_connectionString, _executor, onConnected, onError).Execute();
        }

        public void Execute(Action<NpgsqlConnection> handler, Action<Exception> onError)
        {
            new ExecuteWorker(_connectionString, _executor, handler, onError).Execute();
        }

        public void ReleaseConnection(NpgsqlConnection conn)
        {
            conn.Close();
        }

        private class OpenConnectionWorker
        {
            private readonly string _connectionString;
            private readonly IQueueExecution _executor;
            private readonly Action<NpgsqlConnection> _onConnected;
            private readonly Action<Exception> _onError;
            private readonly NpgsqlConnection _conn;
            private IDisposable _busy;

            public OpenConnectionWorker(string connectionString, IQueueExecution executor, Action<NpgsqlConnection> onConnected, Action<Exception> onError)
            {
                _connectionString = connectionString;
                _executor = executor;
                _onConnected = onConnected;
                _onError = onError;
                _conn = new NpgsqlConnection();
            }

            public void Execute()
            {
                _busy = _executor.AttachBusyProcess();
                _conn.ConnectionString = _connectionString;
                _conn.OpenAsync().ContinueWith(OpenCompleted);
            }
            private void OpenCompleted(Task task)
            {
                if (task.Exception != null)
                    _executor.Enqueue(_onError,task.Exception.GetBaseException());
                else
                    _executor.Enqueue(new OpenConnectionFinished(_onConnected,_conn));
                _busy.Dispose();
            }
        }

        private class OpenConnectionFinished : IQueuedExecutionDispatcher
        {
            private Action<NpgsqlConnection> _onConnected;
            private NpgsqlConnection _conn;

            public OpenConnectionFinished(Action<NpgsqlConnection> onConnected, NpgsqlConnection conn)
            {
                this._onConnected = onConnected;
                this._conn = conn;
            }

            public void Execute()
            {
                _onConnected(_conn);
            }
        }

        private class ExecuteWorker
        {
            private readonly string _connectionString;
            private readonly IQueueExecution _executor;
            private readonly Action<NpgsqlConnection> _handler;
            private readonly Action<Exception> _onError;
            private IDisposable _busy;

            public ExecuteWorker(string connectionString, IQueueExecution executor, Action<NpgsqlConnection> handler, Action<Exception> onError)
            {
                _connectionString = connectionString;
                _executor = executor;
                _handler = handler;
                _onError = onError;
            }

            public void Execute()
            {
                _busy = _executor.AttachBusyProcess();
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
                finally
                {
                    _busy.Dispose();
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
