﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using Vydejna.Domain;
using System.Threading;

namespace Vydejna.Tests.EventSourcedTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [TestClass]
    public class ProjectionProcessTests
    {
        private Mock<IProjectionMetadataManager> _metadataMgr;
        private List<ProjectionInstanceMetadata> _allMetadata;
        private TestProjection _projection;
        private TestStreamer _streamer;
        private TestMetadata _metadata;
        private List<EventStoreEvent> _events;
        private ManualResetEventSlim _eventStoreWaits;

        [TestInitialize]
        public void Initialize()
        {
            _events = new List<EventStoreEvent>();
            _streamer = new TestStreamer(_events, WaitForExit);
            _metadataMgr = new Mock<IProjectionMetadataManager>();
            _allMetadata = new List<ProjectionInstanceMetadata>();
            _projection = new TestProjection();
            _metadata = new TestMetadata(_allMetadata);
            _eventStoreWaits = new ManualResetEventSlim();

            _metadataMgr.Setup(m => m.GetProjection("TestProjection")).Returns(() => TaskResult.GetCompletedTask<IProjectionMetadata>(_metadata));
        }

        [TestMethod]
        public void InitialBuild_AsMaster()
        {
            SetupEventStore();
            RunAsMaster();
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("A", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectLastToken("A");
            ExpectInstance("A");
        }

        [TestMethod]
        public void InitialBuild_AsRebuilder()
        {
            SetupEventStore();
            RunAsIdleRebuilder();
            ExpectNoMetadata("A");
            ExpectContents("");
        }

        [TestMethod]
        public void ContinueInitial_AsMaster()
        {
            SetupEventStore();
            _metadata.SetToken("B", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n");
            RunAsMaster();
            ExpectRebuild(ProjectionRebuildType.ContinueRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectLastToken("B");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectInstance("B");
        }

        [TestMethod]
        public void ContinueInitial_AsRebuilder()
        {
            SetupEventStore();

            var originalToken = _events[1].Token;
            var originalContents = "1: e1\r\n2: e2\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild);
            _metadata.SetToken("B", originalToken);
            _metadata.SetMetadata(originalMetadata);
            _projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void RebuildInitial_AsMaster()
        {
            SetupEventStore();
            _metadata.SetToken("B", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.0", "1.0", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n");
            RunAsMaster();
            ExpectRebuild(ProjectionRebuildType.NewRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectLastToken("B");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectInstance("B");
        }

        [TestMethod]
        public void RebuildInitial_AsRebuilder()
        {
            SetupEventStore();

            var originalToken = _events[1].Token;
            var originalContents = "1: e1\r\n2: e2\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.0", "1.0", null, ProjectionStatus.NewBuild);
            _metadata.SetToken("B", originalToken);
            _metadata.SetMetadata(originalMetadata);
            _projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void ContinueRunning_AsMaster()
        {
            SetupEventStore();
            _metadata.SetToken("B", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");
            RunAsMaster();
            ExpectRebuild(ProjectionRebuildType.NoRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectLastToken("B");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectInstance("B");
        }

        [TestMethod]
        public void ContinueRunning_AsRebuilder()
        {
            SetupEventStore();

            var originalToken = _events[3].Token;
            var originalContents = "1: e1\r\n2: e2\r\n1: e3\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running);
            _metadata.SetToken("B", originalToken);
            _metadata.SetMetadata(originalMetadata);
            _projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void RebuildRunningOnly_AsMaster()
        {
            SetupEventStore();

            _metadata.SetToken("B", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");

            RunAsMasterContinuable();
            _metadata.UpdateStatus("B", ProjectionStatus.Legacy);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.3", "1.3", null, ProjectionStatus.Running));
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsMasterFinish();

            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Legacy));
            ExpectToken("B", _events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
            ExpectInstance("B");
        }

        [TestMethod]
        public void ParallelRebuild_AsMaster()
        {
            SetupEventStore();

            _metadata.SetToken("B", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            _metadata.SetToken("C", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.3", "1.3", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");

            RunAsMasterContinuable();
            _metadata.UpdateStatus("B", ProjectionStatus.Legacy);
            _metadata.UpdateStatus("C", ProjectionStatus.Running);
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsMasterFinish();

            ExpectRebuild(ProjectionRebuildType.NoRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Legacy));
            ExpectToken("B", _events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
            ExpectInstance("B");
        }

        [TestMethod]
        public void RebuildRunningOnly_AsRebuilder()
        {
            SetupEventStore();

            _metadata.SetToken("C", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));

            RunAsRebuilderContinuable();
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsRebuilderFinish();

            ExpectRebuild(ProjectionRebuildType.NewRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n2: e5\r\n");
            ExpectLastToken("D");
            ExpectMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Legacy));
            ExpectInstance("D");
        }

        [TestMethod]
        public void ContinueRebuild_AsRebuilder()
        {
            SetupEventStore();

            _metadata.SetToken("C", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));
            _metadata.SetToken("D", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n");

            RunAsRebuilderContinuable();
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsRebuilderFinish();

            ExpectRebuild(ProjectionRebuildType.ContinueRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n2: e5\r\n");
            ExpectLastToken("D");
            ExpectMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Legacy));
            ExpectInstance("D");
        }

        [TestMethod]
        public void RestartRebuild_AsRebuilder()
        {
            SetupEventStore();

            _metadata.SetToken("C", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.0", "1.0", null, ProjectionStatus.Running));
            _metadata.SetToken("D", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("D", "1.1", "1.0", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n");

            RunAsRebuilderContinuable();
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsRebuilderFinish();

            ExpectRebuild(ProjectionRebuildType.NewRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n2: e5\r\n");
            ExpectLastToken("D");
            ExpectMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.Running));
            ExpectMetadata(new ProjectionInstanceMetadata("C", "1.0", "1.0", null, ProjectionStatus.Legacy));
            ExpectInstance("D");
        }

        [TestMethod]
        public void RebuildObsoleted_AsRebuilder()
        {
            SetupEventStore();

            _metadata.SetToken("C", _events[3].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));

            RunAsRebuilderContinuable();
            _metadata.SetMetadata(new ProjectionInstanceMetadata("E", "1.3", "1.3", null, ProjectionStatus.NewBuild));
            _metadata.UpdateStatus("D", ProjectionStatus.CancelledBuild);
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsRebuilderFinish();

            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.CancelledBuild));
            ExpectToken("D", _events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
            ExpectInstance("D");
        }


        private void SetupEventStore()
        {
            AddEvent("TestEvent1", "e1");
            AddEvent("TestEvent2", "e2");
            AddEvent("TestEventX", "");
            AddEvent("TestEvent1", "e3");
            AddEventPause();
            AddEvent("TestEvent1", "e4");
        }

        private void ExpectLastToken(string instanceName)
        {
            var actualToken = _metadata.GetToken(instanceName).Result.ToString();
            var expectedToken = _events.Where(e => e != null && e.Type != "TestEventX").Select(e => e.Token.ToString()).LastOrDefault();
            Assert.AreEqual(expectedToken, actualToken, "Token for {0}", instanceName);
        }

        private void ExpectToken(string instanceName, EventStoreToken token)
        {
            var actualToken = _metadata.GetToken(instanceName).Result;
            if (token == null)
                Assert.IsNull(actualToken, "Token for {0}", instanceName);
            else if (actualToken == null)
                Assert.AreEqual(token.ToString(), null, "Token for {0}", instanceName);
            else
                Assert.AreEqual(token.ToString(), actualToken.ToString(), "Token for {0}", instanceName);
        }

        private void ExpectMetadata(ProjectionInstanceMetadata expectedMetadata)
        {
            var actualMetadata = _metadata.GetInstanceMetadata(expectedMetadata.Name).Result;
            Assert.AreEqual(expectedMetadata, actualMetadata, "Metadata for {0}", expectedMetadata.Name);
        }

        private void ExpectNoMetadata(string instanceName)
        {
            var actualMetadata = _metadata.GetInstanceMetadata(instanceName).Result;
            Assert.IsNull(actualMetadata, "Metadata for {0} should not exist", instanceName);
        }

        private void ExpectContents(string expectedProjection)
        {
            Assert.AreEqual(expectedProjection, _projection.Contents.ToString(), "Contents");
        }

        private void ExpectRebuild(ProjectionRebuildType expectedRebuild)
        {
            bool expectedWasRebuilt =
                expectedRebuild == ProjectionRebuildType.Initial ||
                expectedRebuild == ProjectionRebuildType.NewRebuild;
            bool expectedRebuildContinued =
                expectedRebuild == ProjectionRebuildType.Initial ||
                expectedRebuild == ProjectionRebuildType.NewRebuild ||
                expectedRebuild == ProjectionRebuildType.ContinueRebuild;
            Assert.AreEqual(expectedWasRebuilt, _projection.WasRebuilt, "Was rebuilt");
            Assert.AreEqual(expectedRebuildContinued, _projection.WasInRebuildMode, "Rebuild started or continued");
        }

        private void ExpectInstance(string instanceName)
        {
            Assert.AreEqual(instanceName, _projection.InstanceName, "Instance name");
        }

        private void RunAsMaster()
        {
            RunAsMasterContinuable();
            RunAsMasterFinish();
        }

        private void RunAsMasterContinuable()
        {
            _process = new ProjectionProcess(_streamer, _metadataMgr.Object, new TestSerializer());
            _process.Setup(_projection).AsMaster();
            _process.Register<TestEvent1>(_projection);
            _process.Register<TestEvent2>(_projection);
            _processTask = _process.Start();
            _processTask.ContinueWith(t => _eventStoreWaits.Set(), TaskContinuationOptions.OnlyOnFaulted);
            _eventStoreWaits.Wait(1000);
        }

        private void RunAsMasterFinish()
        {
            _process.Stop();
            _processTask.GetAwaiter().GetResult();
        }

        private void RunAsIdleRebuilder()
        {
            var process = new ProjectionProcess(
                new Mock<IEventStreaming>(MockBehavior.Strict).Object,
                _metadataMgr.Object, new TestSerializer());
            process.Setup(_projection).AsRebuilder();
            process.Register<TestEvent1>(_projection);
            process.Register<TestEvent2>(_projection);
            var timeout = new Timer(o => process.Stop(), null, 1000, Timeout.Infinite);
            var task = process.Start();
            task.GetAwaiter().GetResult();
            timeout.Dispose();
        }

        private void RunAsRebuilderContinuable()
        {
            _process = new ProjectionProcess(_streamer, _metadataMgr.Object, new TestSerializer());
            _process.Setup(_projection).AsRebuilder();
            _process.Register<TestEvent1>(_projection);
            _process.Register<TestEvent2>(_projection);
            _processTask = _process.Start();
            _processTask.ContinueWith(t => _eventStoreWaits.Set(), TaskContinuationOptions.OnlyOnFaulted);
            _eventStoreWaits.Wait(1000);
        }

        private void RunAsRebuilderFinish()
        {
            _process.Stop();
            _processTask.GetAwaiter().GetResult();
        }

        private List<TaskCompletionSource<object>> _waitersForEvents = new List<TaskCompletionSource<object>>();
        private ProjectionProcess _process;
        private Task _processTask;

        private Task WaitForExit(CancellationToken token)
        {
            lock (_waitersForEvents)
            {
                var tcs = new TaskCompletionSource<object>();
                _waitersForEvents.Add(tcs);
                token.Register(() => tcs.TrySetCanceled());
                _eventStoreWaits.Set();
                return tcs.Task;
            }
        }

        private void SignalMoreEvents()
        {
            List<TaskCompletionSource<object>> copy;
            lock (_waitersForEvents)
            {
                copy = _waitersForEvents.ToList();
                _waitersForEvents.Clear();
                _eventStoreWaits.Reset();
            }
            foreach (var item in copy)
                item.TrySetResult(null);
        }

        private void AddEvent(string typeName, string data)
        {
            var evt = new EventStoreEvent();
            evt.Body = data;
            evt.Format = "text";
            evt.StreamName = "ALL";
            evt.StreamVersion = _events.Count;
            evt.Token = new EventStoreToken(_events.Count.ToString("00000000"));
            evt.Type = typeName;
            _events.Add(evt);
        }

        private void AddEventPause()
        {
            _events.Add(null);
        }

        private class TestProjection : IProjection, IHandle<TestEvent1>, IHandle<TestEvent2>
        {
            public StringBuilder Contents = new StringBuilder();
            public bool InRebuildMode;

            public Task Handle(TestEvent1 evt)
            {
                Contents.AppendFormat("1: {0}\r\n", evt.Data);
                return TaskResult.GetCompletedTask();
            }
            public Task Handle(TestEvent2 evt)
            {
                Contents.AppendFormat("2: {0}\r\n", evt.Data);
                return TaskResult.GetCompletedTask();
            }

            public ProjectionRebuildType NeedsRebuild(string storedVersion)
            {
                return ProjectionUtils.CheckWriterVersion(storedVersion, GetVersion());
            }

            public string GetVersion()
            {
                return "1.2";
            }

            public string GetMinimalReader()
            {
                return "1.0";
            }

            public int EventsBulkSize()
            {
                return 3;
            }

            private static string abc = "ABCDEFGH";
            private IProjectionProcess _process;
            public bool WasRebuilt;
            public bool WasInRebuildMode;
            public string InstanceName;

            public string GenerateInstanceName(string masterName)
            {
                if (masterName == null)
                    return "A";
                else
                    return abc.Substring(masterName[0] - 'A' + 1, 1);
            }

            public Task SetInstanceName(string instanceName)
            {
                InstanceName = instanceName;
                return TaskResult.GetCompletedTask();
            }

            public Task StartRebuild(bool continuation)
            {
                if (!continuation)
                {
                    Contents.Clear();
                    WasRebuilt = true;
                }
                InRebuildMode = true;
                WasInRebuildMode = true;
                return TaskResult.GetCompletedTask();
            }

            public Task PartialCommit()
            {
                return TaskResult.GetCompletedTask();
            }

            public Task CommitRebuild()
            {
                InRebuildMode = false;
                return TaskResult.GetCompletedTask();
            }

            public Task StopRebuild()
            {
                InRebuildMode = false;
                return TaskResult.GetCompletedTask();
            }

            public bool SupportsProcessServices()
            {
                return true;
            }

            public void SetProcessServices(IProjectionProcess process)
            {
                _process = process;
            }

            public string GetConsumerName()
            {
                return "TestProjection";
            }

            public Task HandleShutdown()
            {
                if (_process != null)
                    _process.CommitProjectionProgress();
                return TaskResult.GetCompletedTask();
            }
        }

        private class TestEvent1
        {
            public string Data;
        }
        private class TestEvent2
        {
            public string Data;
        }

        private class TestStreamer : IEventStreaming
        {
            private IList<EventStoreEvent> _events;
            private Func<CancellationToken, Task> _waitForExit;
            public TestStreamer(IList<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit)
            {
                _events = events ?? new EventStoreEvent[0];
                _waitForExit = waitForExit ?? (c => TaskResult.GetCompletedTask());
            }
            public IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode)
            {
                var typeNames = new HashSet<string>(filter.Select(t => t.Name));
                return new TestStream(_events, _waitForExit, typeNames, token, rebuildMode);
            }
        }

        private class TestStream : IEventStreamingInstance
        {
            private IList<EventStoreEvent> _events;
            private Func<CancellationToken, Task> _waitForExit;
            private HashSet<string> _types;
            private EventStoreToken _token;
            private bool _wasTokenFound;
            private int _position;
            private EventStoreEvent _current;
            private bool _readerInRebuild;
            private bool _eventInRebuild;

            public TestStream(IList<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit, HashSet<string> types, EventStoreToken token, bool rebuildMode)
            {
                _position = 0;
                _events = events;
                _waitForExit = waitForExit;
                _types = types;
                _token = token;
                _readerInRebuild = rebuildMode;
                _wasTokenFound = token == EventStoreToken.Initial;
                _current = null;
                _eventInRebuild = true;
            }

            private bool MoveNext()
            {
                if (_position >= _events.Count)
                    return false;
                _current = _events[_position];
                _position++;
                return true;
            }

            public async Task<EventStoreEvent> GetNextEvent(CancellationToken cancel)
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!_eventInRebuild && _readerInRebuild)
                        throw new InvalidOperationException("GetNextEvent called after it returned null");
                    if (!MoveNext())
                    {
                        await _waitForExit(cancel);
                        continue;
                    }
                    if (_current == null)
                    {
                        _eventInRebuild = false;
                        return _current;
                    }
                    if (!_wasTokenFound)
                    {
                        if (_token == _current.Token)
                            _wasTokenFound = true;
                        continue;
                    }
                    if (!_types.Contains(_current.Type))
                        continue;
                    return _current;
                }
            }
        }

        private class TestSerializer : IEventSourcedSerializer
        {
            public bool HandlesFormat(string format)
            {
                return format == "text";
            }

            public object Deserialize(EventStoreEvent evt)
            {
                switch (evt.Type)
                {
                    case "TestEvent1":
                        return new TestEvent1 { Data = evt.Body };
                    case "TestEvent2":
                        return new TestEvent2 { Data = evt.Body };
                    default:
                        throw new InvalidOperationException();
                }
            }

            public void Serialize(object evt, EventStoreEvent stored)
            {
                throw new NotSupportedException();
            }
        }
    }
}
