using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Text;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class EventProcessSimpleTests
    {
        private TestExecutor _executor;
        private TestMetadataInstance _metadata;
        private TestStreaming _streaming;
        private ICommandSubscriptionManager _subscriptions;
        private EventProcessSimple _process;
        private TestHandler _handler;
        
        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _metadata = new TestMetadataInstance();
            _streaming = new TestStreaming(_executor);
            _subscriptions = new CommandSubscriptionManager();
            _handler = new TestHandler();
            _process = new EventProcessSimple(_metadata, _streaming, _subscriptions)
                .WithTokenFlushing(5);
            _process.Register<TestEvent1>(_handler);
            _process.Register<TestEvent2>(_handler);
            _process.Register<TestEvent3>(_handler);
            _process.Register<TestEvent4>(_handler);
        }

        [TestMethod]
        public void LockNotAvailable()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _executor.Process();
            Assert.IsTrue(_metadata.WaitsForLock, "Waits for lock");
            _process.Handle(new SystemEvents.SystemShutdown());
            _executor.Process();
            Assert.IsFalse(_metadata.WaitsForLock, "Does not wait for lock anymore");
        }

        [TestMethod]
        public void LockedWhenShuttingDown()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _executor.Process();
            _process.Handle(new SystemEvents.SystemShutdown());
            _executor.Process();
            Assert.IsFalse(_metadata.IsLocked, "Lock released");
        }

        [TestMethod]
        public void ErrorWhenReadingMetadata()
        {
            _metadata.FailMode = true;
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _executor.Process();
            Assert.IsFalse(_metadata.IsLocked, "Lock released");
        }

        [TestMethod]
        public void StartReadingStreamFromStart()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _executor.Process();
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken);
        }

        [TestMethod]
        public void StartReadingStreamFromToken()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.Token = new EventStoreToken("10");
            _metadata.SendLock();
            _executor.Process();
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(new EventStoreToken("10"), _streaming.CurrentToken);
        }

        [TestMethod]
        public void WhenOnlyOneEventArrives()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.MarkEndOfStream();
            _executor.Process();
            Assert.AreEqual("1", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(new EventStoreToken("1"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("1"), _metadata.Token, "Metadata token");
        }

        [TestMethod]
        public void AfterProcessingFlushNumberEvents()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent2());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _executor.Process();
            Assert.AreEqual("13221", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
        }

        [TestMethod]
        public void ShutdownWhileProcessingEvents()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent2());
            _executor.Process();
            _process.Handle(new SystemEvents.SystemShutdown());
            _executor.Process();
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _streaming.MarkEndOfStream();
            _executor.Process();
            Assert.AreEqual("132", _handler.Output, "Output");
            Assert.IsFalse(_streaming.IsReading, "Reading");
            Assert.IsFalse(_streaming.IsWaiting, "Waiting");
            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.AreEqual(new EventStoreToken("3"), _metadata.Token, "Metadata token");
        }

        [TestMethod]
        public void ErrorInHandler()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent4());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _executor.Process();
            Assert.AreEqual("1321", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
        }

        private class TestHandler
            : IHandle<CommandExecution<TestEvent1>>
            , IHandle<CommandExecution<TestEvent2>>
            , IHandle<CommandExecution<TestEvent3>>
            , IHandle<CommandExecution<TestEvent4>>
        {
            private StringBuilder _sb = new StringBuilder();

            public string Output { get { return _sb.ToString(); } }

            public void Handle(CommandExecution<TestEvent1> message)
            {
                _sb.Append("1");
                message.OnCompleted();
            }

            public void Handle(CommandExecution<TestEvent2> message)
            {
                _sb.Append("2");
                message.OnCompleted();
            }

            public void Handle(CommandExecution<TestEvent3> message)
            {
                _sb.Append("3");
                message.OnCompleted();
            }

            public void Handle(CommandExecution<TestEvent4> message)
            {
                message.OnError(new FormatException("Error in handler"));
            }
        }

        private class TestEvent { public string Data; }
        private class TestEvent1 : TestEvent { }
        private class TestEvent2 : TestEvent { }
        private class TestEvent3 : TestEvent { }
        private class TestEvent4 : TestEvent { }

    }


}
