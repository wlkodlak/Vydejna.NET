using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.Threading;
using System.Data;
using System.IO;
using log4net;

namespace ServiceLib
{
    public class DatabasePostgres : IDisposable
    {
        private readonly string _connectionString;
        private readonly NotificationWatcher _notifications;
        private int _listenerKey;
        private readonly ITime _time;
        private readonly CircuitBreaker _breaker;
        private readonly string _logName;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.Database");
        private int _concurrentWorkers;

        public const string ConnectionNotification = "__CONNECTION__";

        public DatabasePostgres(string connectionString, ITime time)
        {
            _connectionString = connectionString;
            var connectionElements = new NpgsqlConnectionStringBuilder(connectionString);
            _logName = string.Concat(connectionElements.UserName, "@", connectionElements.Host, ":", connectionElements.Port, "/", connectionElements.Database);
            _time = time;
            _breaker = new CircuitBreaker(time).StartHalfOpen();
            _concurrentWorkers = 0;
            _notifications = new NotificationWatcher(this);
            _notifications.Start();
        }

        public Task Execute(Action<NpgsqlConnection, object> handler, object context)
        {
            return Task.Factory.StartNew(ExecuteWorkerVoid, Tuple.Create(handler, context));
        }

        public Task<T> Query<T>(Func<NpgsqlConnection, object, T> handler, object context)
        {
            return Task.Factory.StartNew<T>(ExecuteWorkerFunc<T>, Tuple.Create(handler, context));
        }

        public void ExecuteSync(Action<NpgsqlConnection> handler)
        {
            ExecuteWorkerVoid(new Tuple<Action<NpgsqlConnection, object>, object>((c, o) => handler(c), null));
        }

        private void ExecuteWorkerVoid(object param)
        {
            var parameters = (Tuple<Action<NpgsqlConnection, object>, object>)param;
            using (var conn = new NpgsqlConnection())
            {
                try
                {
                    Interlocked.Increment(ref _concurrentWorkers);
                    conn.ConnectionString = _connectionString;
                    _breaker.Execute(OpenConnection, conn);
                    ExecuteHandler(parameters.Item1, conn, parameters.Item2);
                }
                finally
                {
                    Interlocked.Decrement(ref _concurrentWorkers);
                }
            }
        }

        private T ExecuteWorkerFunc<T>(object param)
        {
            var parameters = (Tuple<Func<NpgsqlConnection, object, T>, object>)param;
            using (var conn = new NpgsqlConnection())
            {
                try
                {
                    Interlocked.Increment(ref _concurrentWorkers);
                    conn.ConnectionString = _connectionString;
                    _breaker.Execute(OpenConnection, conn);
                    return ExecuteHandler(parameters.Item1, conn, parameters.Item2);
                }
                finally
                {
                    Interlocked.Decrement(ref _concurrentWorkers);
                }
            }
        }

        private void OpenConnection(NpgsqlConnection conn)
        {
            try
            {
                Logger.TraceFormat("Getting connection to {0} ({1} concurrent workers)", 
                    _logName, Thread.VolatileRead(ref _concurrentWorkers));
                conn.Open();
            }
            catch (Exception ex)
            {
                if (DetectTransient(ex))
                    throw new TransientErrorException("DBOPEN", ex);
                else
                    throw;
            }
        }

        private void ExecuteHandler(Action<NpgsqlConnection, object> handler, NpgsqlConnection conn, object context)
        {
            try
            {
                handler(conn, context);
            }
            catch (Exception ex)
            {
                if (DetectTransient(ex))
                    throw new TransientErrorException("DBOPEN", ex);
                else
                    throw;
            }
        }

        private T ExecuteHandler<T>(Func<NpgsqlConnection, object, T> handler, NpgsqlConnection conn, object context)
        {
            try
            {
                return handler(conn, context);
            }
            catch (Exception ex)
            {
                if (DetectTransient(ex))
                    throw new TransientErrorException("DBOPEN", ex);
                else
                    throw;
            }
        }

        public void Listen(string listenName, Action<string> onNotify, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
                return;
            var listener = new Listener(_notifications, Interlocked.Increment(ref _listenerKey), listenName, onNotify, cancel);
            _notifications.AddListener(listener);
            listener.RegisterCancelling();
        }

        public void Notify(string name, string payload)
        {
            _notifications.Notify(name, payload);
        }

        public void Dispose()
        {
            _notifications.Stop();
        }

        private static bool DetectTransient(Exception exception)
        {
            NpgsqlException npgsqlException;
            if (exception is System.IO.IOException)
                return true;
            else if ((npgsqlException = exception as NpgsqlException) != null)
            {
                switch (npgsqlException.Code)
                {
                    default:
                        return false;
                }
            }
            else
                return false;
        }

        private class Listener
        {
            private readonly NotificationWatcher _notifications;
            public readonly string Name;
            public readonly int Key;
            private Action<string> _onNotify;
            private CancellationToken _cancel;
            private TaskFactory _taskFactory;

            public Listener(NotificationWatcher notifications, int key, string listenName, Action<string> onNotify, CancellationToken cancel)
            {
                _notifications = notifications;
                _onNotify = onNotify;
                _cancel = cancel;
                _taskFactory = Task.Factory;
                Key = key;
                Name = listenName;
            }

            public void Notify(string payload)
            {
                if (!_cancel.IsCancellationRequested)
                {
                    _taskFactory.StartNew(OnNotify, payload);
                }
            }

            private void OnNotify(object payload)
            {
                try
                {
                    _onNotify(payload as string);
                }
                catch
                {
                }
            }

            private void Unregister()
            {
                _notifications.RemoveListener(this);
            }

            public void RegisterCancelling()
            {
                if (_cancel.CanBeCanceled)
                    _cancel.Register(Unregister);
            }
        }

        private struct Notification
        {
            public string Name;
            public string Payload;
        }

        private struct ListeningChange
        {
            public bool Add;
            public string Name;
        }

        private class NotificationWatcher
        {
            private readonly DatabasePostgres _parent;
            private readonly object _lock;
            private readonly List<Listener> _listeners;
            private readonly List<Notification> _notifications;
            private readonly List<ListeningChange> _changes;
            private readonly string _connectionString;
            private readonly CancellationTokenSource _cancel;
            private Task _task;
            private bool _useSynchronousNotifications = false;

            public NotificationWatcher(DatabasePostgres parent)
            {
                _parent = parent;
                _lock = new object();
                _listeners = new List<Listener>();
                _notifications = new List<Notification>();
                _changes = new List<ListeningChange>();
                var connString = new NpgsqlConnectionStringBuilder(parent._connectionString);
                connString.SyncNotification = _useSynchronousNotifications;
                _connectionString = connString.ToString();
                _cancel = new CancellationTokenSource();
            }

            private void OnNotified(object sender, NpgsqlNotificationEventArgs e)
            {
                Logger.DebugFormat("Received notification {0}, payload {1}", e.Condition, e.AdditionalInformation);
                OnNotified(e.Condition, e.AdditionalInformation);
            }

            private void OnNotified(string condition, string payload)
            {
                lock (_lock)
                {
                    foreach (var listener in _listeners)
                    {
                        if (listener.Name == condition)
                            listener.Notify(payload);
                    }
                }
            }

            public void AddListener(Listener listener)
            {
                lock (_lock)
                {
                    var add = !_listeners.Any(l => l.Name == listener.Name);
                    _listeners.Add(listener);
                    if (add)
                    {
                        Logger.DebugFormat("Adding listener for {0}", listener.Name);
                        _changes.Add(new ListeningChange { Add = true, Name = listener.Name });
                        Monitor.Pulse(_lock);
                    }
                }
            }

            public void RemoveListener(Listener listener)
            {
                lock (_lock)
                {
                    _listeners.Remove(listener);
                    if (!_listeners.Any(l => l.Name == listener.Name))
                    {
                        Logger.DebugFormat("Removing listener for {0}", listener.Name);
                        _changes.Add(new ListeningChange { Add = true, Name = listener.Name });
                        Monitor.Pulse(_lock);
                    }
                }
            }

            public void Notify(string name, string payload)
            {
                lock (_lock)
                {
                    Logger.DebugFormat("Scheduling notification for {0}", name);
                    _notifications.Add(new Notification { Name = name, Payload = payload });
                    Monitor.Pulse(_lock);
                }
            }

            public void Start()
            {
                _task = Task.Factory.StartNew(NotificationCore, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            public void Stop()
            {
                _cancel.Cancel();
                lock (_lock)
                    Monitor.Pulse(_lock);
                _task.Wait(1000);
                _cancel.Dispose();
            }

            private void NotificationCore()
            {
                try
                {
                    var cancel = _cancel.Token;
                    var attemptNumber = 0;
                    Logger.Debug("Starting notification service");
                    while (!cancel.IsCancellationRequested)
                    {
                        attemptNumber++;
                        using (var conn = new NpgsqlConnection(_connectionString))
                        {
                            conn.Notification += OnNotified;
                            if (OpenConnection(conn, cancel, attemptNumber))
                                attemptNumber = 0;
                            else
                                continue;
                            try
                            {
                                InitialListen(conn);
                                while (!cancel.IsCancellationRequested)
                                {
                                    SendChanges(conn, cancel);
                                }
                            }
                            catch (NpgsqlException ex)
                            {
                                if (ex.Message.Contains("Connection is broken."))
                                {
                                    OnNotified(ConnectionNotification, "DISCONNECTED");
                                    continue;
                                }
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Fatal error in NOTIFY task: " + ex.ToString());
                }
            }

            private bool OpenConnection(NpgsqlConnection conn, CancellationToken cancel, int attemptNumber)
            {
                var attemptStart = _parent._time.GetUtcTime();
                try
                {
                    Logger.DebugFormat("Opening connection for notifications to {0}", _parent._logName);
                    conn.Open();
                    return true;
                }
                catch (IOException)
                {
                    Logger.DebugFormat("Connection could not be established to {0}", _parent._logName);
                    var attemptLength = (int)(_parent._time.GetUtcTime() - attemptStart).TotalMilliseconds;
                    var timeout = Math.Max(0, (attemptNumber == 0 ? 0 : 20000) - attemptLength);
                    if (timeout > 0)
                        cancel.WaitHandle.WaitOne(timeout);
                    return false;
                }
            }

            private void InitialListen(NpgsqlConnection conn)
            {
                var commands = new StringBuilder();
                lock (_lock)
                {
                    _changes.Clear();
                    foreach (var listener in _listeners)
                    {
                        commands.Append("LISTEN ").Append(listener.Name).Append("; ");
                    }
                }
                if (commands.Length > 0)
                {
                    Logger.DebugFormat("Sending LISTEN after restoring connection - {0}", commands);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = commands.ToString();
                        cmd.ExecuteNonQuery();
                    }
                }
                OnNotified(ConnectionNotification, "CONNECTED");
            }

            private void SendChanges(NpgsqlConnection conn, CancellationToken cancel)
            {
                var commands = new StringBuilder();
                List<Notification> notifications;
                bool notificationsCheck;
                lock (_lock)
                {
                    foreach (var change in _changes)
                    {
                        commands.Append(change.Add ? "LISTEN " : "UNLISTEN ").Append(change.Name).Append("; ");
                    }
                    _changes.Clear();

                    notifications = _notifications.ToList();
                    _notifications.Clear();

                    if (commands.Length == 0 && notifications.Count == 0)
                    {
                        Monitor.Wait(_lock, _useSynchronousNotifications ? 5000 : 50);
                    }
                    notificationsCheck = !_useSynchronousNotifications;
                }
                if (commands.Length > 0)
                {
                    Logger.DebugFormat("Sending additional LISTENing changes - {0}", commands);
                    notificationsCheck = false;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = commands.ToString();
                        cmd.ExecuteNonQuery();
                    }
                }
                if (notifications.Count > 0)
                {
                    notificationsCheck = false;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT pg_notify(:name, :payload)";
                        var paramName = cmd.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
                        var paramPayload = cmd.Parameters.Add("payload", NpgsqlTypes.NpgsqlDbType.Text);
                        foreach (var notification in notifications)
                        {
                            Logger.DebugFormat("Sending notification {0}, payload {1}", notification.Name, notification.Payload);
                            paramName.Value = notification.Name;
                            paramPayload.Value = notification.Payload;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                if (notificationsCheck)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
