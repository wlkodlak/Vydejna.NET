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
        protected TestExecutor Executor;
        protected INetworkBus Bus;
        protected Message NullMessage;
        protected ManualResetEventSlim Mre;
        private Message LastMessage;

        [TestInitialize]
        public virtual void Initialize()
        {
            Executor = new TestExecutor();
            NullMessage = CreateMessage("NULL", "");
            Bus = CreateBus();
            Mre = new ManualResetEventSlim();
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
        public void AwaitedMessageIsReceived()
        {
            StartReceiving("testprocess", false);
            SendMessage("Msg", "Awaited", "testprocess");
            ExpectMessage("Msg", "Awaited");
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

        protected void ThrowError(Exception ex)
        {
            throw ex.PreserveStackTrace();
        }

        protected void SendMessage(string type, string body, string target)
        {
            var destination = MessageDestination.For(target, "thisnode");
            var message = CreateMessage(type, body);
            var sendMre = new ManualResetEventSlim();
            Bus.Send(destination, message, () => sendMre.Set(), ThrowError);
            Executor.Process();
            Assert.IsTrue(sendMre.Wait(100), "Message sent");
        }

        protected void StartReceiving(string target, bool nowait)
        {
            Bus.Receive(MessageDestination.For(target, "thisnode"), nowait,
                msg => { LastMessage = msg; Mre.Set(); },
                () => { LastMessage = NullMessage; Mre.Set(); }, 
                ThrowError);
            Executor.Process();
        }

        protected Message EndReceiving()
        {
            for (int i = 0; i < 3; i++)
            {
                if (Mre.Wait(30))
                    break;
                else
                    Executor.Process();
            }
            Assert.IsTrue(Mre.Wait(10), "Response received");
            return LastMessage;
        }

        protected void ExpectMessage(string type, string body)
        {
            var message = EndReceiving();
            if (type == null)
                Assert.AreSame(NullMessage, message, "Expected null message");
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
            return new NetworkBusInMemory(Executor, "thisnode");
        }
    }

    [TestClass]
    public class NetworkBusTestsPostgres : NetworkBusTestsBase
    {
        private List<IDisposable> _disposables;

        private string GetConnectionString()
        {
            var connString = new NpgsqlConnectionStringBuilder();
            connString.Database = "servicelibtest";
            connString.Host = "localhost";
            connString.UserName = "postgres";
            connString.Password = "postgres";
            return connString.ToString();
        }

        [TestInitialize]
        public override void Initialize()
        {
            _disposables = new List<IDisposable>();
            base.Initialize();
        }

        protected override INetworkBus CreateBus()
        {
            var db = new DatabasePostgres(GetConnectionString(), Executor);
            var bus = new NetworkBusPostgres("thisnode", Executor, db);
            _disposables.Add(bus);
            bus.Initialize();
            db.ExecuteSync(DeleteAllDocuments);
            return bus;
        }

        private void DeleteAllDocuments(NpgsqlConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM messages; DELETE FROM messages_subscriptions;";
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
