using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib.Tests.TestUtils;
using System.Threading;
using Npgsql;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public abstract class NetworkBusTestsBase
    {
        protected TestScheduler Scheduler;
        protected INetworkBus Bus;
        protected Message LastMessage;
        protected VirtualTime TimeService;
        private CancellationTokenSource CancelWait;
        private Task<Message> CurrentWait;

        [TestInitialize]
        public void Initialize()
        {
            Scheduler = new TestScheduler();
            TimeService = new VirtualTime();
            InitializeCore();
            Bus = CreateBus();
        }

        protected virtual void InitializeCore()
        {

        }

        [TestMethod]
        public void ReceiveAfterSendReturnsSentMessage()
        {
            SendMessage("Msg1", "Body1", "testprocess");
            StartReceiving("testprocess", false);
            ExpectMessage("Msg1", "Body1");
        }

        [TestMethod]
        public void MessagesAreQueued()
        {
            SendMessage("Msg1", "Body1", "testprocess");
            SendMessage("Msg2", "Body2", "testprocess");
            StartReceiving("testprocess", false);
            ExpectMessage("Msg1", "Body1");
            StartReceiving("testprocess", false);
            ExpectMessage("Msg2", "Body2");
        }

        [TestMethod]
        public void NullMessageIfNowaitAndNoMessagesReady()
        {
            StartReceiving("testprocess", true);
            ExpectMessage(null, null);
        }

        [TestMethod]
        public void NullMessageIfCancelledAndNoMessagesReady()
        {
            StartReceiving("testprocess", false);
            CancelWait.Cancel();
            ExpectMessage(null, null);
        }

        [TestMethod]
        public void AwaitedMessageIsReceived()
        {
            StartReceiving("testprocess", false);
            AdvanceTime(3);
            SendMessage("Msg", "Awaited", "testprocess");
            AdvanceTime(3);
            ExpectMessage("Msg", "Awaited");
        }

        [TestMethod]
        public void SubscribedMessagesAreDeliveredToAllSubscribers()
        {
            Subscribe("Msg", "Queue1");
            Subscribe("Msg", "Queue2");
            SendMessage("Msg", "Body", "SUBSCRIBE");
            StartReceiving("Queue1", true);
            ExpectMessage("Msg", "Body");
            StartReceiving("Queue2", true);
            ExpectMessage("Msg", "Body");
        }

        [TestMethod]
        public void DeleteAllRemovesQueueContents()
        {
            SendMessage("Msg", "Body1", "Queue1");
            SendMessage("Msg", "Body2", "Queue2");
            SendMessage("Msg", "Body3", "Queue1");
            SendMessage("Msg", "Body4", "Queue1");

            DeleteAll("Queue1");

            StartReceiving("Queue1", true);
            ExpectMessage(null, null);
            StartReceiving("Queue2", true);
            ExpectMessage("Msg", "Body2");
        }

        [TestMethod]
        public void DeadLettersAreFoundInDeadLetterQueue()
        {
            SendMessage("Msg", "DeadMessage", "Queue");
            StartReceiving("Queue", true);
            var msg = EndReceiving();

            MarkAsDeadLetter(msg);

            var deadlist = GetAllDeadLetters();
            Assert.AreEqual(1, deadlist.Count, "Deadlist.Count");
            Assert.AreEqual("DeadMessage", deadlist[0].Body, "Deadlist[0].Body");
        }

        [TestMethod]
        public void ProcessedMessagesAreDeleted()
        {
            SendMessage("Msg", "Processed1", "Queue");
            SendMessage("Msg", "Processed2", "Queue");
            SendMessage("Msg", "Processed3", "Queue");
            StartReceiving("Queue", true);
            EndReceiving();

            MarkAsProcessed(LastMessage);

            StartReceiving("Queue", true);
            EndReceiving();

            var remaining = GetReadyInQueue(MessageDestination.For("Queue", "thisnode"));
            var inprogress = GetPartialQueue(MessageDestination.For("Queue", "thisnode"));
            var notprocessed = string.Join(", ", remaining.Concat(inprogress).Select(m => m.Body).OrderBy(b => b));
            Assert.AreEqual("Processed2, Processed3", notprocessed);
        }

        [TestMethod]
        public void UnprocessedMessageIsDeliveredAgain()
        {
            SendMessage("Msg", "Processed1", "Queue");
            SendMessage("Msg", "Processed2", "Queue");
            SendMessage("Msg", "Processed3", "Queue");
            StartReceiving("Queue", true);
            EndReceiving();
            MarkAsProcessed(LastMessage);
            StartReceiving("Queue", true);
            EndReceiving();

            AdvanceTime(400);
            StartReceiving("Queue", true);
            ExpectMessage("Msg", "Processed3");
            AdvanceTime(200);
            StartReceiving("Queue", true);
            ExpectMessage("Msg", "Processed2");
        }


        protected abstract INetworkBus CreateBus();

        protected Message CreateMessage(string type, string body)
        {
            return new Message
            {
                Body = body,
                Format = "text",
                Type = type
            };
        }

        protected void AdvanceTime(int seconds)
        {
            TimeService.SetTime(TimeService.GetUtcTime().AddSeconds(seconds));
            Scheduler.Process();
        }

        protected void MarkAsDeadLetter(Message msg)
        {
            Scheduler.Run(() => Bus.MarkProcessed(msg, MessageDestination.DeadLetters)).Wait();
        }

        protected void MarkAsProcessed(Message msg)
        {
            Scheduler.Run(() => Bus.MarkProcessed(msg, MessageDestination.Processed)).Wait();
        }

        protected abstract List<Message> GetAllDeadLetters();
        protected abstract List<Message> GetReadyInQueue(MessageDestination queue);
        protected abstract List<Message> GetPartialQueue(MessageDestination queue);

        protected void SendMessage(string type, string body, string target)
        {
            var destination = target == "SUBSCRIBE" ? MessageDestination.Subscribers : MessageDestination.For(target, "thisnode");
            var message = CreateMessage(type, body);
            Scheduler.Run(() => Bus.Send(destination, message)).Wait();
        }

        protected void Subscribe(string type, string target)
        {
            var mre = new ManualResetEventSlim();
            var destination = MessageDestination.For(target, "thisnode");
            Scheduler.Run(() => Bus.Subscribe(type, destination, false)).Wait();
        }

        protected void DeleteAll(string target)
        {
            var mre = new ManualResetEventSlim();
            var destination = MessageDestination.For(target, "thisnode");
            Scheduler.Run(() => Bus.DeleteAll(destination)).Wait();
        }

        protected void StartReceiving(string target, bool nowait)
        {
            var destination = MessageDestination.For(target, "thisnode");
            CancelWait = new CancellationTokenSource();
            CurrentWait = Scheduler.Run(() => Bus.Receive(destination, nowait, CancelWait.Token), false);
        }

        protected Message EndReceiving()
        {
            Scheduler.Process();
            Assert.IsTrue(CurrentWait.IsCompleted, "Completed");
            LastMessage = CurrentWait.Result;
            return LastMessage;
        }

        protected void ExpectMessage(string type, string body)
        {
            var message = EndReceiving();
            if (type == null)
            {
                Assert.IsNull(message, "No result");
            }
            else
            {
                Assert.IsNotNull(message, "Null message");
                Assert.AreEqual(type, message.Type, "Message type");
                Assert.AreEqual(body, message.Body, "Message body");
            }
        }
    }

    [TestClass]
    public class NetworkBusTestsInMemory : NetworkBusTestsBase
    {
        protected override INetworkBus CreateBus()
        {
            return new NetworkBusInMemory(TimeService);
        }

        protected override List<Message> GetAllDeadLetters()
        {
            var rawBus = (NetworkBusInMemory)Bus;
            return rawBus.GetContents(MessageDestination.DeadLetters);
        }

        protected override List<Message> GetPartialQueue(MessageDestination queue)
        {
            var rawBus = (NetworkBusInMemory)Bus;
            return rawBus.GetContentsInProgress(queue);
        }

        protected override List<Message> GetReadyInQueue(MessageDestination queue)
        {
            var rawBus = (NetworkBusInMemory)Bus;
            return rawBus.GetContents(queue);
        }
    }

    [TestClass]
    public class NetworkBusTestsPostgres : NetworkBusTestsBase
    {
        private List<IDisposable> _disposables;
        private DatabasePostgres _db;
        private string _tableMessages = "bus_messages";
        private string _tableSubscriptions = "bus_messages";

        private string GetConnectionString()
        {
            var connString = new NpgsqlConnectionStringBuilder();
            connString.Database = "servicelibtest";
            connString.Host = "localhost";
            connString.UserName = "postgres";
            connString.Password = "postgres";
            return connString.ToString();
        }

        protected override void InitializeCore()
        {
            _db = new DatabasePostgres(GetConnectionString(), TimeService);
            Scheduler.AllowWaiting(4, 50);
            _disposables = new List<IDisposable>();
        }

        protected override INetworkBus CreateBus()
        {
            var bus = new NetworkBusPostgres("thisnode", _db, TimeService);
            _disposables.Add(bus);
            bus.Initialize();
            _db.ExecuteSync(DeleteAllDocuments);
            return bus;
        }

        private void DeleteAllDocuments(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM " + _tableMessages + "; DELETE FROM " + _tableSubscriptions + ";";
                cmd.ExecuteNonQuery();
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
        }

        private MessageDestination LoadDestination;
        private bool LoadPartial;
        private List<Message> LastLoad;

        protected override List<Message> GetAllDeadLetters()
        {
            LoadDestination = MessageDestination.DeadLetters;
            LoadPartial = false;
            _db.ExecuteSync(PerformLoad);
            return LastLoad;
        }

        protected override List<Message> GetPartialQueue(MessageDestination queue)
        {
            LoadDestination = queue;
            LoadPartial = true;
            _db.ExecuteSync(PerformLoad);
            return LastLoad;
        }

        protected override List<Message> GetReadyInQueue(MessageDestination queue)
        {
            LoadDestination = queue;
            LoadPartial = false;
            _db.ExecuteSync(PerformLoad);
            return LastLoad;
        }

        private void PerformLoad(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT messageid, corellationid, createdon, source, type, format, body, original " +
                    "FROM " + _tableMessages + " WHERE node = :node AND destination = :destination " +
                    "AND processing IS " + (LoadPartial ? "NOT " : "") + "NULL ORDER BY id";
                cmd.Parameters.AddWithValue("node", LoadDestination.NodeId);
                cmd.Parameters.AddWithValue("destination", LoadDestination.ProcessName);
                using (var reader = cmd.ExecuteReader())
                {
                    LastLoad = new List<Message>();
                    while (reader.Read())
                    {
                        var message = new Message();
                        message.MessageId = reader.GetString(0);
                        message.CorellationId = reader.IsDBNull(1) ? null : reader.GetString(1);
                        message.CreatedOn = reader.GetDateTime(2);
                        message.Source = reader.IsDBNull(3) ? null : reader.GetString(3);
                        message.Destination = LoadDestination;
                        message.Type = reader.GetString(4);
                        message.Format = reader.GetString(5);
                        message.Body = reader.GetString(6);
                        LastLoad.Add(message);
                    }
                }
            }
        }
    }
}
