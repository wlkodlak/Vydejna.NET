﻿using Npgsql;
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
        private ConcurrentDictionary<MessageDestination, DestinationCache> _receiving;

        public NetworkBusPostgres(string nodeId, IQueueExecution executor, DatabasePostgres database)
        {
            _nodeId = nodeId;
            _executor = executor;
            _database = database;
            _receiving = new ConcurrentDictionary<MessageDestination, DestinationCache>();
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
                var worker = new SendWorker(_executor, destination, message, onComplete, onError);
                _database.Execute(worker.DoWork, onError);
            }
        }

        public void Receive(MessageDestination destination, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
        {
            DestinationCache cache;
            if (!_receiving.TryGetValue(destination, out cache))
                cache = _receiving.GetOrAdd(destination, new DestinationCache(this, destination));
            var worker = new ReceiveWorker(nowait, onReceived, nothingNew, onError);
            cache.Receive(worker);
        }

        public void Subscribe(string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
        {
            var worker = new SubscribeWorker(_executor, type, destination, unsubscribe, onComplete, onError);
            _database.Execute(worker.DoWork, onError);
        }

        public void MarkProcessed(Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
        {
            var worker = new MarkProcessedWorker(_executor, message, newDestination, onComplete, onError);
            _database.Execute(worker.DoWork, onError);
        }

        public void DeleteAll(MessageDestination destination, Action onComplete, Action<Exception> onError)
        {
            var worker = new DeleteAllWorker(_executor, destination, onComplete, onError);
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
                cmd.CommandText = "SELECT relname FROM pg_catalog.pg_class WHERE relkind = 'r' AND relname IN ('messages', 'messages_subscriptions')";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        switch (reader.GetString(0))
                        {
                            case "messages":
                                tableMessagesExists = true;
                                break;
                            case "messages_subscriptions":
                                tableSubscriptionsExists = true;
                                break;
                        }
                    }
                }
            }

            if (!tableMessagesExists)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS messages (" +
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
                        "processing varchar)";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE UNIQUE INDEX ON messages (node, destination, id)";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE UNIQUE INDEX ON messages (messageid)";
                    cmd.ExecuteNonQuery();
                }
            }

            if (!tableSubscriptionsExists)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS messages_subscriptions (" +
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
            private IQueueExecution _executor;
            private MessageDestination _destination;
            private Message _message;
            private Action _onComplete;
            private Action<Exception> _onError;

            public SendWorker(IQueueExecution executor, MessageDestination destination, Message message, Action onComplete, Action<Exception> onError)
            {
                _executor = executor;
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
                    cmd.CommandText = "SELECT destination, node FROM messages_subscriptions WHERE type = :type";
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
                        "INSERT INTO messages (messageid, corellationid, createdon, source, node, destination, type, format, body) " +
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

            private static void Notify(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "NOTIFY messages";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private class DestinationCache
        {
            private Queue<Message> _cachedMessages;
            private bool _isLoading;
            private List<ReceiveWorker> _waiters;
            private NetworkBusPostgres _parent;
            private MessageDestination _destination;
            private bool _isListening;
            private bool _isNotified;

            public DestinationCache(NetworkBusPostgres parent, MessageDestination destination)
            {
                _cachedMessages = new Queue<Message>();
                _waiters = new List<ReceiveWorker>();
                _parent = parent;
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
                        _parent._executor.Enqueue(new NetworkBusReceiveFinished(worker.OnReceived, message));
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
                    _parent._database.Listen("messages", OnNotify);
                _parent._database.Execute(TryRetrieve, ErrorRetrieve);
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
                _parent._database.Execute(TryRetrieve, ErrorRetrieve);
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
                                _parent._executor.Enqueue(new NetworkBusReceiveFinished(waiter.OnReceived, message));
                            }
                            else if (waiter.Nowait)
                            {
                                _waiters[i] = null;
                                _parent._executor.Enqueue(waiter.NothingNew);
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
                    cmd.CommandText =
                        "SELECT messageid, corellationid, createdon, source, type, format, body, original " +
                        "FROM messages WHERE node = :node AND destination = :destination " + 
                        "AND processing IS NULL ORDER BY id LIMIT 20 FOR UPDATE";
                    cmd.Parameters.AddWithValue("node", _destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
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
                    cmd.CommandText = "UPDATE messages SET processing = :processing WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("processing", _parent._nodeId);
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
                        _parent._executor.Enqueue(waiter.OnError, exception);
                    _waiters.Clear();
                }
            }
        }

        private class ReceiveWorker
        {
            public readonly bool Nowait;
            public readonly Action<Message> OnReceived;
            public readonly Action NothingNew;
            public readonly Action<Exception> OnError;

            public ReceiveWorker(bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError)
            {
                Nowait = nowait;
                OnReceived = onReceived;
                NothingNew = nothingNew;
                OnError = onError;
            }
        }

        private class SubscribeWorker
        {
            private IQueueExecution _executor;
            private string _type;
            private MessageDestination _destination;
            private bool _unsubscribe;
            private Action _onComplete;
            private Action<Exception> _onError;

            public SubscribeWorker(IQueueExecution executor, string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError)
            {
                _executor = executor;
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
                        cmd.CommandText = "DELETE FROM messages_subscriptions WHERE type = :type AND node = :node AND destination = :destination";
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
                            cmd.CommandText = "INSERT INTO messages_subscriptions (type, node, destination) VALUES (:type, :node, :destination)";
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
            private Message _message;
            private MessageDestination _newDestination;
            private Action _onComplete;
            private Action<Exception> _onError;

            public MarkProcessedWorker(IQueueExecution executor, Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError)
            {
                _executor = executor;
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
                        cmd.CommandText = "DELETE FROM messages WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", _message.MessageId);
                        cmd.ExecuteNonQuery();
                    }
                }
                else if (_newDestination == MessageDestination.DeadLetters)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "UPDATE messages SET original = destination, node = '__SPECIAL__', " +
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
                        cmd.CommandText = "UPDATE messages SET node = :node, destination = :destination, processing = NULL WHERE messageid = :messageid";
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
            private IQueueExecution _executor;
            private MessageDestination _destination;
            private Action _onComplete;
            private Action<Exception> _onError;

            public DeleteAllWorker(IQueueExecution executor, MessageDestination destination, Action onComplete, Action<Exception> onError)
            {
                _executor = executor;
                _destination = destination;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void DoWork(NpgsqlConnection conn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM messages WHERE node = :node AND destination = :destination";
                    cmd.Parameters.AddWithValue("node", _destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", _destination.ProcessName);
                    cmd.ExecuteNonQuery();
                }
                _executor.Enqueue(_onComplete);
            }
        }

    }
}
