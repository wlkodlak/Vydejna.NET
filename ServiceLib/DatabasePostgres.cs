﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.Threading;
using System.Data;

namespace ServiceLib
{
    public class DatabasePostgres : IDisposable
    {
        private string _connectionString;
        private IQueueExecution _executor;
        private NotificationWatcher _notifications;
        private int _listenerKey;

        public DatabasePostgres(string connectionString, IQueueExecution executor)
        {
            _connectionString = connectionString;
            _executor = executor;
            _notifications = new NotificationWatcher(this);
        }

        public void OpenConnection(Action<NpgsqlConnection> onConnected, Action<Exception> onError)
        {
            new OpenConnectionWorker(_connectionString, _executor, onConnected, onError).Execute();
        }

        public void Execute(Action<NpgsqlConnection> handler, Action<Exception> onError)
        {
            new ExecuteWorker(_connectionString, _executor, handler, onError).Execute();
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

        public void ReleaseConnection(NpgsqlConnection conn)
        {
            conn.Close();
        }

        public IDisposable Listen(string listenName, Action onNotify)
        {
            var listener = new Listener(_notifications, Interlocked.Increment(ref _listenerKey), listenName, onNotify);
            _notifications.OpenListener();
            _notifications.AddListener(listener);
            return listener;
        }

        public void Dispose()
        {
            _notifications.Dispose();
        }

        private enum ListenerState
        {
            New, BeingAdded, Active, Obsolete, BeingRemoved, Removed
        }

        private class Listener : IDisposable
        {
            private readonly NotificationWatcher _notifications;
            private readonly Action _onNotify;
            public readonly string Name;
            public readonly int Key;
            public ListenerState State;

            public Listener(NotificationWatcher notifications, int key, string listenName, Action onNotify)
            {
                _notifications = notifications;
                _onNotify = onNotify;
                Key = key;
                Name = listenName;
            }

            public void Notify()
            {
                _onNotify();
            }

            public void Dispose()
            {
                _notifications.RemoveListener(this);
            }
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
                    _executor.Enqueue(_onError, task.Exception.GetBaseException());
                else
                    _executor.Enqueue(new OpenConnectionFinished(_onConnected, _conn));
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

        private class NotificationWatcher : IDisposable
        {
            private DatabasePostgres _parent;
            private object _lock;
            private Dictionary<int, Listener> _listeners;
            private NpgsqlConnection _connection;
            private bool _isBusy, _isDisposed;
            private ConnectionState _state;

            public NotificationWatcher(DatabasePostgres parent)
            {
                _parent = parent;
                _lock = new object();
                _listeners = new Dictionary<int, Listener>();
                _connection = new NpgsqlConnection();
                _connection.Notification += OnNotified;
                var connString = new NpgsqlConnectionStringBuilder(parent._connectionString);
                connString.SyncNotification = true;
                _connection.ConnectionString = connString.ToString();
                _state = ConnectionState.Closed;
            }

            private void OnNotified(object sender, NpgsqlNotificationEventArgs e)
            {
                lock (_lock)
                {
                    foreach (var listener in _listeners.Values)
                    {
                        if (listener.Name == e.Condition)
                            _parent._executor.Enqueue(listener.Notify);
                    }
                }
            }

            public void OpenListener()
            {
                lock (_lock)
                {
                    _state = _connection.State;
                    if (_isBusy || _isDisposed)
                        return;
                    if (_connection.State != ConnectionState.Broken && _connection.State != ConnectionState.Closed)
                        return;
                    _isBusy = true;
                    Task.Factory.StartNew(OpenListenerWorker);
                }
            }
            private void OpenListenerWorker()
            {
                try
                {
                    if (_state == ConnectionState.Broken)
                        _connection.Close();
                    _connection.Open();
                }
                catch (Exception exception)
                {
                    OnError(exception);
                    return;
                }
                RefreshNotifications();
            }

            public void AddListener(Listener listener)
            {
                lock (_lock)
                {
                    _listeners.Add(listener.Key, listener);
                    listener.State = ListenerState.New;
                    if (_isBusy)
                        return;
                    _isBusy = true;
                }
                RefreshNotifications();
            }

            public void RemoveListener(Listener listener)
            {
                lock (_lock)
                {
                    if (_state == ConnectionState.Broken || _state == ConnectionState.Closed)
                        listener.State = ListenerState.Removed;
                    else
                        listener.State = ListenerState.Obsolete;
                    if (_isBusy)
                        return;
                    _isBusy = true;
                }
                RefreshNotifications();
            }

            private void RefreshNotifications()
            {
                while (true)
                {
                    var anythingToProcess = false;
                    var commands = new StringBuilder();
                    lock (_lock)
                    {
                        if (!_isDisposed)
                        {
                            foreach (var listener in _listeners)
                            {
                                if (listener.Value.State == ListenerState.New)
                                {
                                    commands.Append("LISTEN ").Append(listener.Value.Name).Append("; ");
                                    listener.Value.State = ListenerState.BeingAdded;
                                    anythingToProcess = true;
                                }
                                else if (listener.Value.State == ListenerState.Obsolete)
                                {
                                    commands.Append("UNLISTEN ").Append(listener.Value.Name).Append("; ");
                                    listener.Value.State = ListenerState.BeingRemoved;
                                    anythingToProcess = true;
                                }
                                else if (listener.Value.State == ListenerState.BeingAdded)
                                {
                                    listener.Value.State = ListenerState.Active;
                                    _parent._executor.Enqueue(listener.Value.Notify);
                                }
                                else if (listener.Value.State == ListenerState.BeingRemoved)
                                    listener.Value.State = ListenerState.Removed;
                            }
                        }
                        if (!anythingToProcess)
                        {
                            _isBusy = false;
                            Monitor.PulseAll(_lock);
                            return;
                        }
                    }
                    try
                    {
                        using (var cmd = _connection.CreateCommand())
                        {
                            cmd.CommandText = commands.ToString();
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception exception)
                    {
                        OnError(exception);
                        return;
                    }
                }
            }

            private void OnError(Exception exception)
            {
                lock (_lock)
                {
                    _state = _connection.State;
                    var broken = _connection.State == ConnectionState.Closed || _connection.State == ConnectionState.Broken;
                    foreach (var listener in _listeners.Values)
                    {
                        if (listener.State == ListenerState.BeingAdded || listener.State == ListenerState.Active)
                            listener.State = ListenerState.New;
                        else if (listener.State == ListenerState.BeingRemoved || listener.State == ListenerState.Obsolete)
                            listener.State = broken ? ListenerState.Removed : ListenerState.Obsolete;
                    }
                    _isBusy = false;
                    Monitor.PulseAll(_lock);
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    _isDisposed = true;
                    while (_isBusy)
                        Monitor.Wait(_lock);
                    _state = ConnectionState.Closed;
                }
                _connection.Close();
            }
        }
    }
}
