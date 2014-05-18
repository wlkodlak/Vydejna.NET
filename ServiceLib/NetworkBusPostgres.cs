﻿using Npgsql;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ServiceLib
{
    public class NetworkBusPostgres : INetworkBus
    {
        private string _nodeId;
        private DatabasePostgres _database;
        private ITime _timeService;
        public int _deliveryTimeout;
        private string _tableMessages = "bus_messages";
        private string _tableSubscriptions = "bus_subscriptions";
        private string _notificationName = "bus";
        private int _canStartListening;
        private CancellationTokenSource _cancelListening;
        private List<ReceiveWaiter> _waiters;

        public NetworkBusPostgres(string nodeId, DatabasePostgres database, ITime timeService)
        {
            _nodeId = nodeId;
            _database = database;
            _timeService = timeService;
            _deliveryTimeout = 600;
            _canStartListening = 1;
            _cancelListening = new CancellationTokenSource();
            _waiters = new List<ReceiveWaiter>();
        }

        public void Dispose()
        {
            _canStartListening = 0;
            _cancelListening.Cancel();
            _cancelListening.Dispose();
        }

        public void Initialize()
        {
            _database.ExecuteSync(InitializeDatabase);
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

        public Task Send(MessageDestination destination, Message message)
        {
            if (destination == MessageDestination.Processed)
                return TaskUtils.CompletedTask();
            else
            {
                message.Destination = destination;
                message.CreatedOn = _timeService.GetUtcTime();
                message.MessageId = Guid.NewGuid().ToString("N");
                message.Source = _nodeId;
                return _database.Execute(SendWorker, new SendParameters(destination, message));
            }
        }

        private class SendParameters
        {
            public readonly MessageDestination Destination;
            public readonly Message Message;

            public SendParameters(MessageDestination destination, Message message)
            {
                Destination = destination;
                Message = message;
            }
        }

        private void SendWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (SendParameters)objContext;
            if (context.Destination == MessageDestination.Subscribers)
            {
                var destinations = FindDestinations(conn, context.Message.Type);
                if (destinations.Count == 0)
                    return;
                foreach (var destination in destinations)
                    InsertMessage(conn, Guid.NewGuid().ToString("N"), destination, context.Message);
                SendNotification(conn);
            }
            else
            {
                InsertMessage(conn, context.Message.MessageId, context.Destination, context.Message);
                SendNotification(conn);
            }
        }

        private List<MessageDestination> FindDestinations(NpgsqlConnection conn, string type)
        {
            var list = new List<MessageDestination>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT destination, node FROM " + _tableSubscriptions + " WHERE type = :type";
                cmd.Parameters.AddWithValue("type", type);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        list.Add(MessageDestination.For(reader.GetString(0), reader.GetString(1)));
                }
            }
            return list;
        }

        private void InsertMessage(NpgsqlConnection conn, string messageId, MessageDestination destination, Message message)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO " + _tableMessages + " (messageid, corellationid, createdon, source, node, destination, type, format, body) " +
                    "VALUES (:messageid, :corellationid, 'now'::timestamp, :source, :node, :destination, :type, :format, :body)";
                cmd.Parameters.AddWithValue("messageid", messageId);
                cmd.Parameters.AddWithValue("corellationid", message.CorellationId);
                cmd.Parameters.AddWithValue("source", message.Source);
                cmd.Parameters.AddWithValue("node", destination.NodeId);
                cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                cmd.Parameters.AddWithValue("type", message.Type);
                cmd.Parameters.AddWithValue("format", message.Format);
                cmd.Parameters.AddWithValue("body", message.Body);
                cmd.ExecuteNonQuery();
            }
        }

        private void SendNotification(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "NOTIFY " + _notificationName;
                cmd.ExecuteNonQuery();
            }
        }

        public Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
                return TaskUtils.CancelledTask<Message>();
            if (Interlocked.Exchange(ref _canStartListening, 0) == 1)
            {
                _timeService.Delay(5000, _cancelListening.Token).ContinueWith(Receive_Timer);
                _database.Listen(_notificationName, Receive_Notification, _cancelListening.Token);
            }
            return TaskUtils.FromEnumerable<Message>(ReceiveInternal(new ReceiveWaiter(destination, nowait, cancel))).GetTask();
        }

        private class ReceiveWaiter
        {
            public MessageDestination Destination;
            public bool Nowait;
            public CancellationToken Cancel;
            public AutoResetEventAsync Event;
            public CancellationTokenRegistration CancelRegistration;

            public ReceiveWaiter(MessageDestination destination, bool nowait, CancellationToken cancel)
            {
                Destination = destination;
                Nowait = nowait;
                Cancel = cancel;
                if (!Nowait)
                {
                    Event = new AutoResetEventAsync();
                }
            }
        }

        private void Receive_Timer(Task task)
        {
            if (task.Exception == null)
            {
                List<ReceiveWaiter> waiters;
                lock (_waiters)
                    waiters = _waiters.ToList();
                foreach (var waiter in waiters)
                    waiter.Event.Set();
                _timeService.Delay(5000, _cancelListening.Token).ContinueWith(Receive_Timer);
            }
        }

        private void Receive_Notification(string payload)
        {
            List<ReceiveWaiter> waiters;
            lock (_waiters)
                waiters = _waiters.ToList();
            foreach (var waiter in waiters)
                waiter.Event.Set();
        }

        private void Receive_Cancelled(object param)
        {
            var waiter = (ReceiveWaiter)param;
            waiter.Event.Set();
        }

        private IEnumerable<Task> ReceiveInternal(ReceiveWaiter waiter)
        {
            var taskGetMessage = _database.Query(Receive_GetMessage, waiter.Destination);
            yield return taskGetMessage;
            if (taskGetMessage.Exception != null || taskGetMessage.IsCanceled || taskGetMessage.Result != null || waiter.Nowait)
                yield break;
            if (waiter.Cancel.CanBeCanceled)
            {
                waiter.CancelRegistration = waiter.Cancel.Register(Receive_Cancelled, waiter);
            }

            lock (_waiters)
                _waiters.Add(waiter);
            while (true)
            {
                var taskWait = waiter.Event.Wait();
                yield return taskWait;

                if (waiter.Cancel.IsCancellationRequested)
                {
                    lock (_waiters)
                        _waiters.Remove(waiter);
                    yield return TaskUtils.FromResult<Message>(null);
                    yield break;
                }

                taskGetMessage = _database.Query(Receive_GetMessage, waiter.Destination);
                yield return taskGetMessage;

                if (taskGetMessage.Exception != null || taskGetMessage.IsCanceled || taskGetMessage.Result != null)
                {
                    waiter.CancelRegistration.Dispose();
                    lock (_waiters)
                        _waiters.Remove(waiter);
                    yield break;
                }
            }
        }

        private Message Receive_GetMessage(NpgsqlConnection conn, object objContext)
        {
            var destination = (MessageDestination)objContext;
            using (var tran = conn.BeginTransaction())
            {
                Message message;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT messageid, corellationid, createdon, source, type, format, body FROM " + _tableMessages +
                        " WHERE node = :node AND destination = :destination AND (processing IS NULL OR processing <= :since) " +
                        "ORDER BY id LIMIT 1 FOR UPDATE";
                    cmd.Parameters.AddWithValue("node", destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                    cmd.Parameters.AddWithValue("since", _timeService.GetUtcTime().AddSeconds(-_deliveryTimeout));
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;
                        else
                        {
                            message = new Message();
                            message.Destination = destination;
                            message.MessageId = reader.GetString(0);
                            message.CorellationId = reader.IsDBNull(1) ? null : reader.GetString(1);
                            message.CreatedOn = reader.GetDateTime(2);
                            message.Source = reader.IsDBNull(3) ? null : reader.GetString(3);
                            message.Type = reader.GetString(4);
                            message.Format = reader.GetString(5);
                            message.Body = reader.GetString(6);
                        }
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE " + _tableMessages + " SET processing = :now WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("now", _timeService.GetUtcTime());
                    cmd.Parameters.AddWithValue("messageid", message.MessageId);
                    cmd.ExecuteReader();
                }
                tran.Commit();
                return message;
            }
        }

        public Task Subscribe(string type, MessageDestination destination, bool unsubscribe)
        {
            return _database.Execute(SubscribeWorker, new SubscribeParameters(type, destination, unsubscribe));
        }

        private class SubscribeParameters
        {
            public readonly string Type;
            public readonly MessageDestination Destination;
            public readonly bool Unsubscribe;

            public SubscribeParameters(string type, MessageDestination destination, bool unsubscribe)
            {
                Type = type;
                Destination = destination;
                Unsubscribe = unsubscribe;
            }
        }

        public void SubscribeWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (SubscribeParameters)objContext;
            if (context.Unsubscribe)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM " + _tableSubscriptions + " WHERE type = :type AND node = :node AND destination = :destination";
                    cmd.Parameters.AddWithValue("type", context.Type);
                    cmd.Parameters.AddWithValue("node", context.Destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", context.Destination.ProcessName);
                    cmd.ExecuteNonQuery();
                }
            }
            else
            {
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO " + _tableSubscriptions + " (type, node, destination) VALUES (:type, :node, :destination)";
                        cmd.Parameters.AddWithValue("type", context.Type);
                        cmd.Parameters.AddWithValue("node", context.Destination.NodeId);
                        cmd.Parameters.AddWithValue("destination", context.Destination.ProcessName);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (NpgsqlException dbex)
                {
                    if (dbex.Code != "23505")
                        throw;
                }
            }
        }


        public Task MarkProcessed(Message message, MessageDestination newDestination)
        {
            return _database.Execute(MarkProcessedWorker, new MarkProcessedParameters(message, newDestination));
        }

        private class MarkProcessedParameters
        {
            public readonly Message Message;
            public readonly MessageDestination NewDestination;

            public MarkProcessedParameters(Message message, MessageDestination newDestination)
            {
                Message = message;
                NewDestination = newDestination;
            }
        }

        private void MarkProcessedWorker(NpgsqlConnection conn, object objContext)
        {
            var context = (MarkProcessedParameters)objContext;
            if (context.NewDestination == MessageDestination.Processed)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM " + _tableMessages + " WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("messageid", context.Message.MessageId);
                    cmd.ExecuteNonQuery();
                }
            }
            else if (context.NewDestination == MessageDestination.DeadLetters)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "UPDATE " + _tableMessages + " SET original = destination, node = '__SPECIAL__', " +
                        "destination = 'deadletters', processing = NULL WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("messageid", context.Message.MessageId);
                    cmd.Parameters.AddWithValue("node", context.NewDestination.NodeId);
                    cmd.Parameters.AddWithValue("destination", context.NewDestination.ProcessName);
                    cmd.ExecuteNonQuery();
                }
            }
            else
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE " + _tableMessages + " SET node = :node, destination = :destination, processing = NULL WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("messageid", context.Message.MessageId);
                    cmd.Parameters.AddWithValue("node", context.NewDestination.NodeId);
                    cmd.Parameters.AddWithValue("destination", context.NewDestination.ProcessName);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public Task DeleteAll(MessageDestination destination)
        {
            return _database.Execute(DeleteAllWorker, destination);
        }

        private void DeleteAllWorker(NpgsqlConnection conn, object objContext)
        {
            var destination = (MessageDestination)objContext;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM " + _tableMessages + " WHERE node = :node AND destination = :destination";
                cmd.Parameters.AddWithValue("node", destination.NodeId);
                cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
