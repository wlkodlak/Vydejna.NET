using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class EventProjectorSimpleTests
    {
        private TestScheduler _scheduler;
        private TestProjection _projection;
        private TestMetadataInstance _metadata;
        private TestStreaming _streaming;
        private CommandSubscriptionManager _subscriptions;
        private EventProjectorSimple _process;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _projection = new TestProjection();
            _metadata = new TestMetadataInstance();
            _streaming = new TestStreaming();
            _subscriptions = new CommandSubscriptionManager();
            _process = new EventProjectorSimple(_projection, _metadata, _streaming, _subscriptions);
            _process.Init(null, _scheduler);
            _process.Register<ProjectorMessages.Reset>(_projection);
            _process.Register<ProjectorMessages.UpgradeFrom>(_projection);
            _process.Register<ProjectorMessages.RebuildFinished>(_projection);
            _process.Register<ProjectorMessages.Flush>(_projection);
            _process.Register<TestEvent1>(_projection);
            _process.Register<TestEvent2>(_projection);
            _process.Register<TestEvent3>(_projection);
        }

        [TestMethod]
        public void WaitingForLockEndsOnShutdown()
        {
            _process.Start();
            _scheduler.Process();
            _process.Pause();
            _scheduler.Process();
            Assert.IsFalse(_metadata.WaitsForLock, "WaitsForLock");
            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
        }

        [TestMethod]
        public void WhenProjectionIsTooOld()
        {
            _metadata.Version = "0.8";
            _metadata.Token = new EventStoreToken("333");
            _process.Start();
            _scheduler.Process();
            
            _metadata.SendLock();
            _scheduler.Process();

            Assert.AreEqual("0.8", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("reset", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void WhenProjectionNeedsUpgrade()
        {
            _metadata.Version = "0.9";
            _metadata.Token = new EventStoreToken("333");
            _process.Start();
            _scheduler.Process();

            _metadata.SendLock();
            _scheduler.Process();

            Assert.AreEqual("0.9", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("upgrade", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(new EventStoreToken("333"), _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void WhenProjectionIsUpToDate()
        {
            _metadata.Version = "1.0";
            _metadata.Token = new EventStoreToken("333");
            _process.Start();
            _scheduler.Process();

            _metadata.SendLock();
            _scheduler.Process();

            Assert.AreEqual("1.0", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("normal", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(new EventStoreToken("333"), _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void UnlockOnShutdown()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _process.Pause();
            _scheduler.Process();
            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.IsFalse(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsDisposed, "Streaming IsDisposed");
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
        }

        [TestMethod]
        public void NotifyAboutFinishedRebuild()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();

            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("3"), _metadata.Token, "Metadata token");
            CollectionAssert.Contains(_projection.LogEntries, "RebuildFinished");
        }

        [TestMethod]
        public void NotifyAboutEndOfStream()
        {
            _metadata.Version = "1.0";
            _metadata.Token = EventStoreToken.Initial;
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();

            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("3"), _metadata.Token, "Metadata token");
            CollectionAssert.Contains(_projection.LogEntries, "Flush");
        }

        [TestMethod]
        public void NotifyAboutEveryNonemptyFlush()
        {
            _metadata.Version = "1.0";
            _metadata.Token = EventStoreToken.Initial;
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _streaming.AddEvent("4", new TestEvent2 { Data = "87" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _streaming.MarkEndOfStream();
            _scheduler.Process();

            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("4"), _metadata.Token, "Metadata token");
            Assert.AreEqual(2, _projection.LogEntries.Count(s => s == "Flush"), "Flush count");
        }

        [TestMethod]
        public void StopOnError()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();
            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent3 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent2 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();

            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.IsFalse(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsDisposed, "Streaming IsDisposed");
        }

        [TestMethod]
        public void CallsEventHandlers()
        {
            _process.Start();
            _scheduler.Process();
            _metadata.SendLock();
            _scheduler.Process();
            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent2 { Data = "75" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _streaming.AddEvent("3", new TestEvent2 { Data = "32" });
            _streaming.AddEvent("4", new TestEvent1 { Data = "14" });
            _streaming.MarkEndOfStream();
            _scheduler.Process();
            _process.Pause();
            _scheduler.Process();

            Assert.AreEqual(
                "Reset\r\nTestEvent1: 47\r\nTestEvent2: 75\r\nRebuildFinished\r\nFlush\r\n" +
                "TestEvent2: 32\r\nTestEvent1: 14\r\nFlush", 
                _projection.LogText);
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
        }

        private class TestEvent { public string Data;}
        private class TestEvent1 : TestEvent { }
        private class TestEvent2 : TestEvent { }
        private class TestEvent3 : TestEvent { }
        private class TestEvent4 : TestEvent { }

        private class TestProjection
            : IEventProjection
            , IProcess<ProjectorMessages.Flush>
            , IProcess<ProjectorMessages.RebuildFinished>
            , IProcess<TestEvent1>
            , IProcess<TestEvent2>
            , IProcess<TestEvent3>
        {
            public string Mode = "normal";
            public List<string> LogEntries = new List<string>();
            public string OriginalVersion;

            private Task ProcessCommand<T>(T message)
            {
                LogEntries.Add(typeof(T).Name);
                return TaskUtils.CompletedTask();
            }

            private Task ProcessEvent<T>(T message)
                where T : TestEvent
            {
                LogEntries.Add(string.Concat(typeof(T).Name, ": " + message.Data));
                return TaskUtils.CompletedTask();
            }

            public string GetVersion()
            {
                return "1.0";
            }

            public string LogText
            {
                get { return string.Join("\r\n", LogEntries); }
            }

            public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
            {
                OriginalVersion = storedVersion;
                if (storedVersion == "1.0")
                    return EventProjectionUpgradeMode.NotNeeded;
                else if (storedVersion == "0.9")
                    return EventProjectionUpgradeMode.Upgrade;
                else
                    return EventProjectionUpgradeMode.Rebuild;
            }

            public Task Handle(ProjectorMessages.Reset message)
            {
                Mode = "reset";
                return ProcessCommand(message);
            }

            public Task Handle(ProjectorMessages.UpgradeFrom message)
            {
                Mode = "upgrade";
                return ProcessCommand(message);
            }

            public Task Handle(ProjectorMessages.RebuildFinished message)
            {
                return ProcessCommand(message);
            }

            public Task Handle(ProjectorMessages.Flush message)
            {
                return ProcessCommand(message);
            }

            public Task Handle(TestEvent1 message)
            {
                return ProcessEvent(message);
            }

            public Task Handle(TestEvent2 message)
            {
                return ProcessEvent(message);
            }

            public Task Handle(TestEvent3 message)
            {
                return TaskUtils.FromError<object>(new Exception("Handler error"));
            }
        }
    }
}
