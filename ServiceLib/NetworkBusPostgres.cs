using Npgsql;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class NetworkBusPostgres : INetworkBus
    {
        private string _nodeId;
        private IQueueExecution _executor;
        private DatabasePostgres _database;
        private ITime _timeService;
        private ConcurrentDictionary<MessageDestination, DestinationCache> _receiving;
        public int _deliveryTimeout;
        private string _tableMessages = "bus_messages";
        private string _tableSubscriptions = "bus_subscriptions";
        private string _notificationName = "bus";

        public NetworkBusPostgres(string nodeId, IQueueExecution executor, DatabasePostgres database, ITime timeService)
        {
            _nodeId = nodeId;
            _executor = executor;
            _database = database;
            _timeService = timeService;
            _receiving = new ConcurrentDictionary<MessageDestination, DestinationCache>();
            _deliveryTimeout = 600;
        }

        public void Initialize()
        {
            _database.ExecuteSync(InitializeDatabase);
        }

        public void Send(MessageDestination destination, Message message, Action onComplete, Action<Exception> onError)
        {
            if (destination == MessageDestination.Processed)
                _executor.Enqueue(onComplete);
            else
            {
                message.Destination = destination;
                message.CreatedOn = DateTime.UtcNow;
                message.MessageId = Guid.NewGuid().ToString("N");
                message.Source = _nodeId;
                var worker = new SendWorker(this, destination, message, onComplete, onError);
                _database.Execute(worker.DoWork, onError);
            }
        }

        public IDisposable Receive(MessageDestination destination, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
        {
            DestinationCache cache;
            if (!_receiving.TryGetValue(destination, out cache))
                cache = _receiving.GetOrAdd(destination, new DestinationCache(this, destination));
            var worker = new ReceiveWorker(cache, nowait, onReceived, nothingNew, onError);
            cache.Receive(worker);
            return worker;
        }

        public void Subscribe(string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
        {
            var worker = new SubscribeWorker(this, type, destination, unsubscribe, onComplete, onError);
            _database.Execute(worker.DoWork, onError);
        }

        public void MarkProcessed(Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
        {
            var worker = new MarkProcessedWorker(this, message, newDestination, onComplete, onError);
            _database.Execute(worker.DoWork, onError);
        }

        public void DeleteAll(MessageDestination destination, Action onComplete, Action<Exception> onError)
        {
            var worker = new DeleteAllWorker(this, destination, onComplete, onError);
            _database.Execute(worker.DoWork, onError);
        }

        public void Dispose()
        {
        }

        private void InitializeDatabase(NpgsqlConnection conn)
        {
            bool tableMessagesExists = false;
            bool tableSubscriptionsExists = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT relname FROM pg_catalog.pg_class WHERE relkind = 'r' AND relname IN ('",
                    _tableMessages, "', '", _tableSubscriptions, "')");
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var tableName = reader.GetString(0);
                        if (tableName == _tableMessages)
                            tableMessagesExists = true;
                        else if (tableName == _tableSubscriptions)
                            tableSubscriptionsExists = true;
                    }
                }
            }

            if (!tableMessagesExists)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + _tableMessages + " (" +
                        "id serial PRIMARY KEY, " +
                        "messageid varchar NOT NULL, " +
                        "corellationid varchar, " +
                        "createdon timestamp NOT NULL, " +
                        "source varchar, " +
                        "node varchar NOT NULL, " +
                        "destination varchar NOT NULL, " +
                        "original varchar, " +
                        "type varchar NOT NULL, " +
                        "format varchar NOT NULL, " +
                        "body text NOT NULL, " +
                        "processing timestamp)";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE UNIQUE INDEX ON " + _tableMessages + " (node, destination, id)";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE UNIQUE INDEX ON " + _tableMessages + " (messageid)";
                    cmd.ExecuteNonQuery();
                }
            }

            if (!tableSubscriptionsExists)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + _tableSubscriptions + " (" +
                        "type varchar NOT NULL, " +
                        "node varchar NOT NULL, " +
                        "destination varchar NOT NULL, " +
                        "PRIMARY KEY (type, node, destination))";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private class SendWorker
        {
            private NetworkBusPostgres _parent;
            private IQueueExecution _executor;
            private MessageDestination _destination;
            private Message _message;
            private Action _onComplete;
            private Action<Exception> _onError;

            public SendWorker(NetworkBusPostgres parent, MessageDestination destination, Message message, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _executor = parent._executor;
                _destination = destination;
                _message = message;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void DoWork(NpgsqlConnection conn)
            {
                if (_destination == MessageDestination.Subscribers)
                {
                    var destinations = FindDestinations(conn, _message.Type);
                    if (destinations.Count == 0)
                        return;
                    foreach (var destination in destinations)
                        InsertMessage(conn, Guid.NewGuid().ToString("N"), destination);
                    Notify(conn);
                    _executor.Enqueue(_onComplete);
                }
                else
                {
                    InsertMessage(conn, _message.MessageId, _destination);
                    Notify(conn);
                    _executor.Enqueue(_onComplete);
                }
            }

            private List<MessageDestination> FindDestinations(NpgsqlConnection conn, string type)
            {
                var list = new List<MessageDestination>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT destination, node FROM " + _parent._tableSubscriptions + " WHERE type = :type";
                    cmd.Parameters.AddWithValue("type", type);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            list.Add(MessageDestination.For(reader.GetString(0), reader.GetString(1)));
                    }
                }
                return list;
            }

            private void InsertMessage(NpgsqlConnection conn, string messageId, MessageDestination destination)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO " + _parent._tableMessages + " (messageid, corellationid, createdon, source, node, destination, type, format, body) " +
                        "VALUES (:messageid, :corellationid, 'now'::timestamp, :source, :node, :destination, :type, :format, :body)";
                    cmd.Parameters.AddWithValue("messageid", messageId);
                    cmd.Parameters.AddWithValue("corellationid", _message.CorellationId);
                    cmd.Parameters.AddWithValue("source", _message.Source);
                    cmd.Parameters.AddWithValue("node", destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                    cmd.Parameters.AddWithValue("type", _message.Type);
                    cmd.Parameters.AddWithValue("format", _message.Format);
                    cmd.Parameters.AddWithValue("body", _message.Body);
                    cmd.ExecuteNonQuery();
                }
            }

            private void Notify(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "NOTIFY " + _parent._notificationName;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private class DestinationCache
        {
            private Queue<Message> _cachedMessages;
            private bool _isLoading;
            private List<ReceiveWorker> _waiters;
            public NetworkBusPostgres Parent;
            private MessageDestination _destination;
            private bool _isListening;
            private bool _isNotified;

            public DestinationCache(NetworkBusPostgres parent, MessageDestination destination)
            {
                _cachedMessages = new Queue<Message>();
                _waiters = new List<ReceiveWorker>();
                Parent = parent;
                _destination = destination;
            }

            public void Receive(ReceiveWorker worker)
            {
                bool startListening = false;
                lock (this)
                {
                    if (_cachedMessages.Count > 0)
                    {
                        var message = _cachedMessages.Dequeue();
                        Parent._executor.Enqueue(new NetworkBusReceiveFinished(worker.OnReceived, message));
                        return;
                    }
                    if (!_isListening)
                    {
                        _isListening = true;
                        startListening = true;
                    }
                    _waiters.Add(worker);
                    if (_isLoading)
                        return;
                    _isLoading = true;
                }
                if (startListening)
                    Parent._database.Listen(Parent._notificationName, OnNotify);
                Parent._database.Execute(TryRetrieve, ErrorRetrieve);
            }

            private void OnNotify()
            {
                lock (this)
                {
                    _isNotified = true;
                    if (_isLoading)
                        return;
                    _isLoading = true;
                    _isNotified = false;
                }
                Parent._database.Execute(TryRetrieve, ErrorRetrieve);
            }

            private void TryRetrieve(NpgsqlConnection conn)
            {
                List<Message> newMessages;
                bool needsNewLoad = true;
                while (needsNewLoad)
                {
                    needsNewLoad = false;
                    using (var tran = conn.BeginTransaction())
                    {
                        newMessages = LoadMessages(conn);
                        MarkMessagesAsProcessing(conn, newMessages);
                        tran.Commit();
                    }
                    lock (this)
                    {
                        foreach (var message in newMessages)
                            _cachedMessages.Enqueue(message);
                        for (int i = 0; i < _waiters.Count; i++)
                        {
                            var waiter = _waiters[i];
                            if (_cachedMessages.Count > 0)
                            {
                                var message = _cachedMessages.Dequeue();
                                _waiters[i] = null;
                                if (waiter.TryToUse())
                                    Parent._executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message));
                            }
                            else if (waiter.Nowait)
                            {
                                _waiters[i] = null;
                                Parent._executor.Enqueue(waiter.NothingNew);
                            }
                        }
                        RemoveEmptyWaiters();
                        if (_isNotified)
                        {
                            needsNewLoad = true;
                            _isNotified = false;
                        }
                        else
                            _isLoading = false;
                    }
                }
            }

            private void RemoveEmptyWaiters()
            {
                _waiters.RemoveAll(w => w == null);
            }

            private List<Message> LoadMessages(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    var processingLimit = Parent._timeService.GetUtcTime().AddSeconds(-Parent._deliveryTimeout);
                    cmd.CommandText =
                        "SELECT messageid, corellationid, createdon, source, type, format, body, original " +
                        "FROM " + Parent._tableMessages + " WHERE node = :node AND destination = :destination " +
                        "AND (processing IS NULL OR processing <= :processing) ORDER BY id LIMIT 20 FOR UPDATE";
                    cmd.Parameters.AddWithValue("node", _destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
                    cmd.Parameters.AddWithValue("processing", processingLimit);
                    using (var reader = cmd.ExecuteReader())
                    {
                        var list = new List<Message>();
                        while (reader.Read())
                        {
                            var message = new Message();
                            message.MessageId = reader.GetString(0);
                            message.CorellationId = reader.IsDBNull(1) ? null : reader.GetString(1);
                            message.CreatedOn = reader.GetDateTime(2);
                            message.Source = reader.IsDBNull(3) ? null : reader.GetString(3);
                            message.Destination = _destination;
                            message.Type = reader.GetString(4);
                            message.Format = reader.GetString(5);
                            message.Body = reader.GetString(6);
                            list.Add(message);
                        }
                        return list;
                    }
                }
            }

            private void MarkMessagesAsProcessing(NpgsqlConnection conn, List<Message> newMessages)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE " + Parent._tableMessages + " SET processing = :processing WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("processing", Parent._timeService.GetUtcTime());
                    var paramMessageId = cmd.Parameters.Add("messageid", NpgsqlTypes.NpgsqlDbType.Varchar);
                    foreach (var message in newMessages)
                    {
                        paramMessageId.Value = message.MessageId;
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            private void ErrorRetrieve(Exception exception)
            {
                lock (this)
                {
                    _isLoading = false;
                    foreach (var waiter in _waiters)
                        Parent._executor.Enqueue(waiter.OnError, exception);
                    _waiters.Clear();
                }
            }
        }

        private class ReceiveWorker : IDisposable
        {
            public readonly DestinationCache Parent;
            public readonly bool Nowait;
            public readonly Action<Message> OnReceived;
            public readonly Action NothingNew;
            public readonly Action<Exception> OnError;
            public bool Used, Disposed;

            public ReceiveWorker(DestinationCache parent, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
            {
                Parent = parent;
                Nowait = nowait;
                OnReceived = onReceived;
                NothingNew = nothingNew;
                OnError = onError;
            }

            public void Dispose()
            {
                lock (Parent)
                {
                    Disposed = true;
                    if (!Used)
                        Parent.Parent._executor.Enqueue(NothingNew);
                }
            }

            public bool TryToUse()
            {
                if (Disposed || Used)
                    return false;
                else
                {
                    Used = true;
                    return true;
                }
            }
        }

        private class SubscribeWorker
        {
            private IQueueExecution _executor;
            private string _tableSubscriptions;
            private string _type;
            private MessageDestination _destination;
            private bool _unsubscribe;
            private Action _onComplete;
            private Action<Exception> _onError;

            public SubscribeWorker(NetworkBusPostgres parent, string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
            {
                _executor = parent._executor;
                _tableSubscriptions = parent._tableSubscriptions;
                _type = type;
                _destination = destination;
                _unsubscribe = unsubscribe;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void DoWork(NpgsqlConnection conn)
            {
                if (_unsubscribe)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM " + _tableSubscriptions + " WHERE type = :type AND node = :node AND destination = :destination";
                        cmd.Parameters.AddWithValue("type", _type);
                        cmd.Parameters.AddWithValue("node", _destination.NodeId);
                        cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
                        cmd.ExecuteNonQuery();
                    }
                    _executor.Enqueue(_onComplete);
                }
                else
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO " + _tableSubscriptions + " (type, node, destination) VALUES (:type, :node, :destination)";
                            cmd.Parameters.AddWithValue("type", _type);
                            cmd.Parameters.AddWithValue("node", _destination.NodeId);
                            cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
                            cmd.ExecuteNonQuery();
                        }
                        _executor.Enqueue(_onComplete);
                    }
                    catch (NpgsqlException dbex)
                    {
                        if (dbex.Code == "23505")
                            _executor.Enqueue(_onComplete);
                        else
                            throw;
                    }
                }
            }
        }

        private class MarkProcessedWorker
        {
            private IQueueExecution _executor;
            private string _tableMessages;
            private Message _message;
            private MessageDestination _newDestination;
            private Action _onComplete;
            private Action<Exception> _onError;

            public MarkProcessedWorker(NetworkBusPostgres parent, Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
            {
                _executor = parent._executor;
                _tableMessages = parent._tableMessages;
                _message = message;
                _newDestination = newDestination;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void DoWork(NpgsqlConnection conn)
            {
                if (_newDestination == MessageDestination.Processed)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM " + _tableMessages + " WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", _message.MessageId);
                        cmd.ExecuteNonQuery();
                    }
                }
                else if (_newDestination == MessageDestination.DeadLetters)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "UPDATE " + _tableMessages + " SET original = destination, node = '__SPECIAL__', " +
                            "destination = 'deadletters', processing = NULL WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", _message.MessageId);
                        cmd.Parameters.AddWithValue("node", _newDestination.NodeId);
                        cmd.Parameters.AddWithValue("destination", _newDestination.ProcessName);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE " + _tableMessages + " SET node = :node, destination = :destination, processing = NULL WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", _message.MessageId);
                        cmd.Parameters.AddWithValue("node", _newDestination.NodeId);
                        cmd.Parameters.AddWithValue("destination", _newDestination.ProcessName);
                        cmd.ExecuteNonQuery();
                    }
                }
                _executor.Enqueue(_onComplete);
            }
        }

        private class DeleteAllWorker
        {
            private string _tableMessages;
            private IQueueExecution _executor;
            private MessageDestination _destination;
            private Action _onComplete;
            private Action<Exception> _onError;

            public DeleteAllWorker(NetworkBusPostgres parent, MessageDestination destination, Action onComplete, Action<Exception> onError)
            {
                _executor = parent._executor;
                _tableMessages = parent._tableMessages;
                _destination = destination;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void DoWork(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM " + _tableMessages + " WHERE node = :node AND destination = :destination";
                    cmd.Parameters.AddWithValue("node", _destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
                    cmd.ExecuteNonQuery();
                }
                _executor.Enqueue(_onComplete);
            }
        }

    }
}
