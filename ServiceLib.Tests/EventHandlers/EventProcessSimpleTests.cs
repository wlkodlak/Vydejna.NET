using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class EventProcessSimpleTests
    {
        private TestScheduler _scheduler;
        private TestMetadataInstance _metadata;
        private TestStreaming _streaming;
        private ICommandSubscriptionManager _subscriptions;
        private EventProcessSimple _process;
        private TestHandler _handler;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _metadata = new TestMetadataInstance();
            _streaming = new TestStreaming();
            _subscriptions = new CommandSubscriptionManager();
            _handler = new TestHandler();
            _process = new EventProcessSimple(_metadata, _streaming, _subscriptions).WithTokenFlushing(5);
            _process.Register<TestEvent1>(_handler);
            _process.Register<TestEvent2>(_handler);
            _process.Register<TestEvent3>(_handler);
            _process.Register<TestEvent4>(_handler);
            _process.Init(null, _scheduler);
        }

        [TestMethod]
        public void LockNotAvailable()
        {
            _process.Start();
            _scheduler.Process();
            Assert.IsTrue(_metadata.WaitsForLock, "Waits for lock");
            _process.Pause();
            _scheduler.Process();
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
            Assert.IsFalse(_metadata.WaitsForLock, "Does not wait for lock anymore");
        }

        [TestMethod]
        public void LockedWhenShuttingDown()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();
            _process.Pause();
            _scheduler.Process();
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
            Assert.IsFalse(_metadata.IsLocked, "Lock released");
        }

        [TestMethod]
        public void ErrorWhenReadingMetadata()
        {
            _metadata.FailMode = true;
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();
            Assert.IsFalse(_metadata.IsLocked, "Lock released");
        }

        [TestMethod]
        public void StartReadingStreamFromStart()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken);
        }

        [TestMethod]
        public void StartReadingStreamFromToken()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.Token = new EventStoreToken("10");
            _metadata.SendLock();
            _scheduler.Process();
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(new EventStoreToken("10"), _streaming.CurrentToken);
        }

        [TestMethod]
        public void WhenOnlyOneEventArrives()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            Assert.AreEqual("1", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.IsTrue(_streaming.IsWaiting, "Waiting");
            Assert.AreEqual(new EventStoreToken("1"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("1"), _metadata.Token, "Metadata token");
        }

        [TestMethod]
        public void AfterProcessingFlushNumberEvents()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent2());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _scheduler.Process();
            Assert.AreEqual("13221", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
        }

        [TestMethod]
        public void ShutdownWhileProcessingEvents()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent2());
            _scheduler.Process();
            _process.Pause();
            _scheduler.Process();
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            Assert.AreEqual("132", _handler.Output, "Output");
            Assert.IsFalse(_streaming.IsReading, "Reading");
            Assert.IsFalse(_streaming.IsWaiting, "Waiting");
            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.AreEqual(new EventStoreToken("3"), _metadata.Token, "Metadata token");
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
        }

        [TestMethod]
        public void NormalErrorInHandler()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent4());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _scheduler.Process();
            Assert.AreEqual("1321", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
            Assert.AreEqual(1, _streaming.DeadLetters.Count, "Dead letters");
        }

        [TestMethod]
        public void FatalErrorInHandler()
        {
            _handler.ErrorMode = "Fatal";
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent4());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _scheduler.Process();
            Assert.AreEqual("1321", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
            Assert.AreEqual(1, _streaming.DeadLetters.Count, "Dead letters");
        }

        [TestMethod]
        public void TransientErrorInHandler()
        {
            _handler.ErrorMode = "Transient";
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("1", new TestEvent1());
            _streaming.AddEvent("2", new TestEvent3());
            _streaming.AddEvent("3", new TestEvent4());
            _streaming.AddEvent("4", new TestEvent2());
            _streaming.AddEvent("5", new TestEvent1());
            _scheduler.Process();
            Assert.AreEqual("134!421", _handler.Output, "Output");
            Assert.IsTrue(_streaming.IsReading, "Reading");
            Assert.AreEqual(new EventStoreToken("5"), _streaming.CurrentToken, "CurrentToken");
            Assert.AreEqual(new EventStoreToken("5"), _metadata.Token, "Metadata token");
            Assert.AreEqual(0, _streaming.DeadLetters.Count, "Dead letters");
        }

        private class TestHandler
            : IProcess<TestEvent1>
            , IProcess<TestEvent2>
            , IProcess<TestEvent3>
            , IProcess<TestEvent4>
        {
            private StringBuilder _sb = new StringBuilder();

            public string Output { get { return _sb.ToString(); } }
            public string ErrorMode = "Error";

            public Task Handle(TestEvent1 message)
            {
                _sb.Append("1");
                return TaskUtils.CompletedTask();
            }

            public Task Handle(TestEvent2 message)
            {
                _sb.Append("2");
                return TaskUtils.CompletedTask();
            }

            public Task Handle(TestEvent3 message)
            {
                _sb.Append("3");
                return TaskUtils.CompletedTask();
            }

            public Task Handle(TestEvent4 message)
            {
                switch (ErrorMode)
                {
                    case "Fatal":
                        throw new FormatException("Error in handler");
                    case "None":
                        _sb.Append("4");
                        return TaskUtils.CompletedTask();
                    case "Transient":
                        _sb.Append("4!");
                        ErrorMode = "None";
                        return TaskUtils.FromError<object>(new TransientErrorException("TEST", "Test transient error"));
                    case "Error":
                    default:
                        return TaskUtils.FromError<object>(new FormatException("Error in handler"));
                }
            }
        }

        private class TestEvent { }
        private class TestEvent1 : TestEvent { }
        private class TestEvent2 : TestEvent { }
        private class TestEvent3 : TestEvent { }
        private class TestEvent4 : TestEvent { }

    }


}
