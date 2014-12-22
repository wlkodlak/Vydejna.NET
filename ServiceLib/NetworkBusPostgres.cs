using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace ServiceLib
{
    public class NetworkBusPostgres : INetworkBus
    {
        private readonly string _nodeId;
        private readonly DatabasePostgres _database;
        private readonly ITime _timeService;
        public int DeliveryTimeout;
        private const string _tableMessages = "bus_messages";
        private const string _tableSubscriptions = "bus_subscriptions";
        private const string _notificationName = "bus";
        private int _canStartListening;
        private readonly CancellationTokenSource _cancelListening;
        private readonly CancellationToken _cancelListeningToken;
        private readonly List<ReceiveWaiter> _waiters;

        private static readonly NetworkBusPostgresTraceSource Logger =
            new NetworkBusPostgresTraceSource("ServiceLib.NetworkBus");

        // ReSharper disable once NotAccessedField.Local
        private Task _taskTimer;

        public NetworkBusPostgres(string nodeId, DatabasePostgres database, ITime timeService)
        {
            _nodeId = nodeId;
            _database = database;
            _timeService = timeService;
            DeliveryTimeout = 600;
            _canStartListening = 1;
            _cancelListening = new CancellationTokenSource();
            _cancelListeningToken = _cancelListening.Token;
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
            using (new LogMethod(Logger, "InitializeDatabase"))
            {
                var tableMessagesExists = false;
                var tableSubscriptionsExists = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Concat(
                        "SELECT relname FROM pg_catalog.pg_class WHERE relkind = 'r' AND relname IN ('",
                        _tableMessages, "', '", _tableSubscriptions, "')");
                    Logger.TraceSql(cmd);
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
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "CREATE UNIQUE INDEX ON " + _tableMessages + " (node, destination, id)";
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "CREATE UNIQUE INDEX ON " + _tableMessages + " (messageid)";
                        Logger.TraceSql(cmd);
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
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public async Task Send(MessageDestination destination, Message message)
        {
            message.Destination = destination;
            message.CreatedOn = _timeService.GetUtcTime();
            message.MessageId = Guid.NewGuid().ToString("N");
            message.Source = _nodeId;
            if (destination != MessageDestination.Processed)
            {
                await _database.Execute(SendWorker, new SendParameters(destination, message));
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
            using (new LogMethod(Logger, "Send"))
            {
                var context = (SendParameters) objContext;
                if (context.Destination == MessageDestination.Subscribers)
                {
                    var destinations = FindDestinations(conn, context.Message.Type);
                    if (destinations.Count == 0)
                        return;
                    foreach (var destination in destinations)
                    {
                        InsertMessage(conn, Guid.NewGuid().ToString("N"), destination, context.Message);
                        Logger.MessageSent(context.Message, destination);
                    }
                    SendNotification(conn);
                }
                else
                {
                    InsertMessage(conn, context.Message.MessageId, context.Destination, context.Message);
                    Logger.MessageSent(context.Message, context.Destination);
                    SendNotification(conn);
                }
            }
        }

        private List<MessageDestination> FindDestinations(NpgsqlConnection conn, string type)
        {
            var list = new List<MessageDestination>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT destination, node FROM " + _tableSubscriptions + " WHERE type = :type";
                cmd.Parameters.AddWithValue("type", type);
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        list.Add(MessageDestination.For(reader.GetString(0), reader.GetString(1)));
                }
            }
            Logger.FoundDestinations(type, list);
            return list;
        }

        private void InsertMessage(
            NpgsqlConnection conn, string messageId, MessageDestination destination, Message message)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO " + _tableMessages +
                    " (messageid, corellationid, createdon, source, node, destination, type, format, body) " +
                    "VALUES (:messageid, :corellationid, 'now'::timestamp, :source, :node, :destination, :type, :format, :body)";
                cmd.Parameters.AddWithValue("messageid", messageId);
                cmd.Parameters.AddWithValue("corellationid", message.CorellationId);
                cmd.Parameters.AddWithValue("source", message.Source);
                cmd.Parameters.AddWithValue("node", destination.NodeId);
                cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                cmd.Parameters.AddWithValue("type", message.Type);
                cmd.Parameters.AddWithValue("format", message.Format);
                cmd.Parameters.AddWithValue("body", message.Body);
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private void SendNotification(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "NOTIFY " + _notificationName;
                Logger.TraceSql(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        public async Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            StartReceivingTimer();
            var waiter = new ReceiveWaiter(destination, nowait, cancel);

            using (new LogMethod(Logger, "Receive"))
            {
                try
                {
                    var receivedMessage = await _database.Query(Receive_GetMessage, waiter.Destination);
                    if (receivedMessage != null)
                    {
                        Logger.MessageReceived(destination, receivedMessage);
                        return receivedMessage;
                    }
                    if (nowait)
                    {
                        Logger.NoMessageAvailable(destination);
                        return null;
                    }

                    Logger.StartedWaitingForMessage(destination);
                    if (waiter.Cancel.CanBeCanceled)
                    {
                        waiter.Cancel.Register(Receive_Cancelled, waiter);
                    }

                    lock (_waiters)
                        _waiters.Add(waiter);
                    while (true)
                    {
                        try
                        {
                            await waiter.Event.Wait();
                        }
                        catch (OperationCanceledException)
                        {
                            lock (_waiters)
                                _waiters.Remove(waiter);
                        }

                        receivedMessage = await _database.Query(Receive_GetMessage, waiter.Destination);
                        if (receivedMessage != null)
                        {
                            Logger.MessageReceived(destination, receivedMessage);
                            return receivedMessage;
                        }
                        else
                        {
                            Logger.NoMessageAvailableYet(waiter.Destination);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.ReceiveCancelled(destination);
                    throw;
                }
                catch (Exception exception)
                {
                    Logger.ReceiveFailed(destination, exception);
                    throw;
                }
            }
        }

        private void StartReceivingTimer()
        {
            if (Interlocked.Exchange(ref _canStartListening, 0) == 1)
            {
                _taskTimer = Receive_Timer();
                _database.Listen(_notificationName, Receive_Notification, _cancelListeningToken);
            }
        }

        private class ReceiveWaiter
        {
            public readonly MessageDestination Destination;
            public CancellationToken Cancel;
            public readonly AutoResetEventAsync Event;

            public ReceiveWaiter(MessageDestination destination, bool nowait, CancellationToken cancel)
            {
                Destination = destination;
                Cancel = cancel;
                if (!nowait)
                {
                    Event = new AutoResetEventAsync();
                }
            }
        }

        private async Task Receive_Timer()
        {
            while (!_cancelListeningToken.IsCancellationRequested)
            {
                await _timeService.Delay(5000, _cancelListeningToken);
                var anyMessages = await _database.Query(Receive_CheckAnyMessages, null);
                if (anyMessages)
                {
                    List<ReceiveWaiter> waiters;
                    lock (_waiters)
                        waiters = _waiters.ToList();
                    foreach (var waiter in waiters)
                        waiter.Event.Set();
                }
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
            var waiter = (ReceiveWaiter) param;
            waiter.Event.Set();
        }

        private bool Receive_CheckAnyMessages(NpgsqlConnection conn, object objContext)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Concat(
                    "SELECT 1 FROM ", _tableMessages,
                    " WHERE (processing IS NULL OR processing <= :since) LIMIT 1");
                cmd.Parameters.AddWithValue("since", _timeService.GetUtcTime().AddSeconds(-DeliveryTimeout));
                Logger.TraceSql(cmd);
                using (var reader = cmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        private Message Receive_GetMessage(NpgsqlConnection conn, object objContext)
        {
            var destination = (MessageDestination) objContext;
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
                    cmd.Parameters.AddWithValue("since", _timeService.GetUtcTime().AddSeconds(-DeliveryTimeout));
                    Logger.TraceSql(cmd);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;
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
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE " + _tableMessages + " SET processing = :now WHERE messageid = :messageid";
                    cmd.Parameters.AddWithValue("now", _timeService.GetUtcTime());
                    cmd.Parameters.AddWithValue("messageid", message.MessageId);
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
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
            using (new LogMethod(Logger, "Subscribe"))
            {
                var context = (SubscribeParameters) objContext;
                try
                {
                    if (context.Unsubscribe)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "DELETE FROM " + _tableSubscriptions +
                                              " WHERE type = :type AND node = :node AND destination = :destination";
                            cmd.Parameters.AddWithValue("type", context.Type);
                            cmd.Parameters.AddWithValue("node", context.Destination.NodeId);
                            cmd.Parameters.AddWithValue("destination", context.Destination.ProcessName);
                            Logger.TraceSql(cmd);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        try
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "INSERT INTO " + _tableSubscriptions +
                                                  " (type, node, destination) VALUES (:type, :node, :destination)";
                                cmd.Parameters.AddWithValue("type", context.Type);
                                cmd.Parameters.AddWithValue("node", context.Destination.NodeId);
                                cmd.Parameters.AddWithValue("destination", context.Destination.ProcessName);
                                Logger.TraceSql(cmd);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (NpgsqlException dbex)
                        {
                            if (dbex.Code != "23505")
                                throw;
                        }
                    }
                    Logger.Subscribed(context.Unsubscribe, context.Destination, context.Type);
                }
                catch (Exception exception)
                {
                    Logger.SubscribeFailed(context.Unsubscribe, context.Destination, context.Type, exception);
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
            using (new LogMethod(Logger, "MarkProcessed"))
            {
                var context = (MarkProcessedParameters) objContext;
                if (context.NewDestination == MessageDestination.Processed)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM " + _tableMessages + " WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", context.Message.MessageId);
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    Logger.MarkedAsProcessed(context.Message.Destination, context.Message);
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
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    Logger.MarkedAsDeadLetter(context.Message.Destination, context.Message);
                }
                else
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE " + _tableMessages +
                                          " SET node = :node, destination = :destination, processing = NULL WHERE messageid = :messageid";
                        cmd.Parameters.AddWithValue("messageid", context.Message.MessageId);
                        cmd.Parameters.AddWithValue("node", context.NewDestination.NodeId);
                        cmd.Parameters.AddWithValue("destination", context.NewDestination.ProcessName);
                        Logger.TraceSql(cmd);
                        cmd.ExecuteNonQuery();
                    }
                    Logger.MessageMoved(context.Message.Destination, context.Message, context.NewDestination);
                }
            }
        }

        public Task DeleteAll(MessageDestination destination)
        {
            return _database.Execute(DeleteAllWorker, destination);
        }

        private void DeleteAllWorker(NpgsqlConnection conn, object objContext)
        {
            using (new LogMethod(Logger, "DeleteAll"))
            {
                var destination = (MessageDestination) objContext;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM " + _tableMessages +
                                      " WHERE node = :node AND destination = :destination";
                    cmd.Parameters.AddWithValue("node", destination.NodeId);
                    cmd.Parameters.AddWithValue("destination", destination.ProcessName);
                    Logger.TraceSql(cmd);
                    cmd.ExecuteNonQuery();
                }
                Logger.DeletedAllMessages(destination.ProcessName);
            }
        }
    }

    public class NetworkBusPostgresTraceSource : TraceSource
    {
        public NetworkBusPostgresTraceSource(string name)
            : base(name)
        {
        }


        public void WaiterCancelled(string processName, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 27, "Waiter {TaskId} cancelled");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void MessageDelivered(string processName, Message message, int taskId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 25, "Message was delivered to waiter");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void MessageNotDeliveredToCurrentWaiter(string processName, Message messageId, int taskId)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 26, "Attempt to deliver message failed, will be retried");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("MessageId", false, messageId);
            msg.SetProperty("TaskId", false, taskId);
            msg.Log(this);
        }

        public void DeletedAllMessages(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 30, "Deleted all messages");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void MessageBroadcasted(Message message, MessageDestination destination)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 21, "Message {MessageId} sent to {Destination} using broadcast");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void MessageSent(Message message, MessageDestination destination)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 22, "Message {MessageId} sent to {Destination} directly");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void MessageReceived(MessageDestination destination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 11, "Message {MessageId} received");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("Type", false, message.Type);
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void NoMessageAvailable(MessageDestination destination)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 12, "No messages in queue");
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void NoMessageAvailableYet(MessageDestination destination)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 12, "No messages in queue yet");
            msg.SetProperty("Destination", false, destination.ProcessName);
            msg.Log(this);
        }

        public void StartedWaitingForMessage(MessageDestination destination)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 13, "Started waiting for message");
            msg.SetProperty("ProcessName", false, destination.ProcessName);
            msg.Log(this);
        }

        public void Subscribed(bool unsubscribe, MessageDestination destination, string type)
        {
            var summary = unsubscribe
                ? "Process {ProcessName} unsubscribed from {Type}"
                : "Process {ProcessName} subscribed to {Type}";
            var msg = new LogContextMessage(TraceEventType.Verbose, 16, summary);
            msg.SetProperty("Action", false, unsubscribe ? "Unsubscribed" : "Subscribed");
            msg.SetProperty("Type", false, type);
            msg.SetProperty("ProcessName", false, destination.ProcessName);
            msg.Log(this);
        }

        public void SubscribeFailed(bool unsubscribe, MessageDestination destination, string type, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 46, "Subscription failed");
            msg.SetProperty("Action", false, unsubscribe ? "Unsubscribed" : "Subscribed");
            msg.SetProperty("Type", false, type);
            msg.SetProperty("ProcessName", false, destination.ProcessName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void MarkedAsDeadLetter(MessageDestination originalDestination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 17, "Message {MessageId} marked as dead letter");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("OriginalDestination", false, originalDestination.ProcessName);
            msg.Log(this);
        }

        public void MarkedAsProcessed(MessageDestination originalDestination, Message message)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 18, "Message {MessageId} marked as processed");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("OriginalDestination", false, originalDestination.ProcessName);
            msg.Log(this);
        }

        public void FoundDestinations(string type, List<MessageDestination> destinations)
        {
            var destinationsString = new StringBuilder();
            var processedDestinations = 0;
            foreach (var destination in destinations)
            {
                if (processedDestinations > 0)
                {
                    destinationsString.Append(", ");
                }
                if (processedDestinations >= 20)
                {
                    destinationsString.Append("...");
                    break;
                }
                destinationsString.Append(destination);
                processedDestinations++;
            }

            var msg = new LogContextMessage(TraceEventType.Verbose, 41, "Found {Count} destinations for type {Type}");
            msg.SetProperty("Type", false, type);
            msg.SetProperty("Count", false, destinations.Count);
            msg.SetProperty("Destinations", false, destinationsString);
        }

        public void MessageMoved(MessageDestination destination, Message message, MessageDestination newDestination)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 48, "Message {MessageId} moved from {OriginalDestination} to {NewDestination}");
            msg.SetProperty("MessageId", false, message.MessageId);
            msg.SetProperty("OriginalDestination", false, destination);
            msg.SetProperty("NewDestination", false, destination);
            msg.Log(this);
        }

        public void ReceiveFailed(MessageDestination destination, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 49, "Receive from {Destination} failed");
            msg.SetProperty("Destination", false, destination);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ReceiveCancelled(MessageDestination destination)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 50, "Receive from {Destination} cancelled");
            msg.SetProperty("Destination", false, destination);
            msg.Log(this);
        }
    }
}