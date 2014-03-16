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
        private TestExecutor _executor;
        private TestProjection _projection;
        private TestMetadataInstance _metadata;
        private TestStreaming _streaming;
        private CommandSubscriptionManager _subscriptions;
        private EventProjectorSimple _process;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _projection = new TestProjection();
            _metadata = new TestMetadataInstance();
            _streaming = new TestStreaming(_executor);
            _subscriptions = new CommandSubscriptionManager();
            _process = new EventProjectorSimple(_projection, _metadata, _streaming, _subscriptions);
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
            _executor.Process();
            _process.Pause();
            _executor.Process();
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
            
            _metadata.SendLock();
            _executor.Process();

            Assert.AreEqual("0.8", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("reset", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.IsFalse(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void WhenProjectionNeedsUpgrade()
        {
            _metadata.Version = "0.9";
            _metadata.Token = new EventStoreToken("333");
            _process.Start();

            _metadata.SendLock();
            _executor.Process();

            Assert.AreEqual("0.9", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("upgrade", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("333"), _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void WhenProjectionIsUpToDate()
        {
            _metadata.Version = "1.0";
            _metadata.Token = new EventStoreToken("333");
            _process.Start();

            _metadata.SendLock();
            _executor.Process();

            Assert.AreEqual("1.0", _projection.OriginalVersion, "OriginalVersion");
            Assert.AreEqual("normal", _projection.Mode, "Mode");
            Assert.AreEqual("1.0", _metadata.Version, "Version");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("333"), _streaming.CurrentToken, "Streaming token");
        }

        [TestMethod]
        public void UnlockOnShutdown()
        {
            _process.Start();
            _metadata.SendLock();
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _process.Pause();
            _executor.Process();
            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.IsFalse(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsDisposed, "Streaming IsDisposed");
            Assert.AreEqual(ProcessState.Inactive, _process.State, "Process state");
        }

        [TestMethod]
        public void NotifyAboutFinishedRebuild()
        {
            _process.Start();
            _metadata.SendLock();
            _executor.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();

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
            _metadata.SendLock();
            _executor.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();

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
            _metadata.SendLock();
            _executor.Process();

            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent1 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent1 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.AddEvent("4", new TestEvent2 { Data = "87" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.MarkEndOfStream();
            _executor.Process();

            Assert.IsTrue(_streaming.IsWaiting, "IsWaiting");
            Assert.AreEqual(new EventStoreToken("4"), _metadata.Token, "Metadata token");
            Assert.AreEqual(2, _projection.LogEntries.Count(s => s == "Flush"), "Flush count");
        }

        [TestMethod]
        public void StopOnError()
        {
            _process.Start();
            _metadata.SendLock();
            _executor.Process();
            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent3 { Data = "32" });
            _streaming.AddEvent("3", new TestEvent2 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();

            Assert.IsFalse(_metadata.IsLocked, "IsLocked");
            Assert.IsFalse(_streaming.IsReading, "IsReading");
            Assert.IsTrue(_streaming.IsDisposed, "Streaming IsDisposed");
        }

        [TestMethod]
        public void CallsEventHandlers()
        {
            _process.Start();
            _metadata.SendLock();
            _executor.Process();
            _streaming.AddEvent("1", new TestEvent1 { Data = "47" });
            _streaming.AddEvent("2", new TestEvent2 { Data = "75" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.AddEvent("3", new TestEvent2 { Data = "32" });
            _streaming.AddEvent("4", new TestEvent1 { Data = "14" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _process.Pause();
            _executor.Process();

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
            , IHandle<CommandExecution<ProjectorMessages.Flush>>
            , IHandle<CommandExecution<ProjectorMessages.RebuildFinished>>
            , IHandle<CommandExecution<TestEvent1>>
            , IHandle<CommandExecution<TestEvent2>>
            , IHandle<CommandExecution<TestEvent3>>
        {
            public string Mode = "normal";
            public List<string> LogEntries = new List<string>();
            public string OriginalVersion;

            private void ProcessCommand<T>(CommandExecution<T> message)
            {
                LogEntries.Add(typeof(T).Name);
                message.OnCompleted();
            }

            private void ProcessEvent<T>(CommandExecution<T> message)
                where T : TestEvent
            {
                LogEntries.Add(string.Concat(typeof(T).Name, ": " + message.Command.Data));
                message.OnCompleted();
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

            public void Handle(CommandExecution<ProjectorMessages.Reset> message)
            {
                Mode = "reset";
                ProcessCommand(message);
            }

            public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
            {
                Mode = "upgrade";
                ProcessCommand(message);
            }

            public void Handle(CommandExecution<ProjectorMessages.RebuildFinished> message)
            {
                ProcessCommand(message);
            }

            public void Handle(CommandExecution<ProjectorMessages.Flush> message)
            {
                ProcessCommand(message);
            }

            public void Handle(CommandExecution<TestEvent1> message)
            {
                ProcessEvent(message);
            }

            public void Handle(CommandExecution<TestEvent2> message)
            {
                ProcessEvent(message);
            }

            public void Handle(CommandExecution<TestEvent3> message)
            {
                message.OnError(new Exception("Handler error"));
            }
        }
    }
}
