using System;
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
            AddEvent("TestEvent1", "e1");
            AddEvent("TestEvent2", "e2");
            AddEvent("TestEvent1", "e3");
            AddEventPause();
            AddEvent("TestEvent1", "e4");

            var process = new ProjectionProcess(_streamer, _metadataMgr.Object, new TestSerializer());
            process.Setup(_projection).AsMaster();
            process.Register<TestEvent1>(_projection);
            process.Register<TestEvent2>(_projection);
            var task = process.Start();
            _eventStoreWaits.Wait(1000);
            process.Stop();
            task.GetAwaiter().GetResult();

            var expectedProjection = "1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n";
            var metadata = _metadata.GetInstanceMetadata("A").Result;
            var token = _metadata.GetToken("A").Result.ToString();
            Assert.AreEqual(expectedProjection, _projection.Contents.ToString());
            Assert.AreEqual("1.2", metadata.Version);
            Assert.AreEqual(ProjectionStatus.Running, metadata.Status);
            Assert.AreEqual("00000004", token);
        }

        [TestMethod]
        public void InitialBuild_AsRebuilder()
        {
            AddEvent("TestEvent1", "e1");
            AddEvent("TestEvent2", "e2");
            AddEvent("TestEvent1", "e3");
            AddEventPause();
            AddEvent("TestEvent1", "e4");
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
            var metadata = _metadata.GetInstanceMetadata("A").Result;
            Assert.AreEqual("", _projection.Contents.ToString());
            Assert.IsNull(metadata);
        }

        [TestMethod]
        public void ContinueInitial_AsMaster()
        {
            AddEvent("TestEvent1", "e1");
            AddEvent("TestEvent2", "e2");
            AddEvent("TestEvent1", "e3");
            AddEventPause();
            AddEvent("TestEvent1", "e4");

            _metadata.SetToken("B", _events[1].Token);
            _metadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild));
            _projection.Contents.Append("1: e1\r\n2: e2\r\n");

            var process = new ProjectionProcess(_streamer, _metadataMgr.Object, new TestSerializer());
            process.Setup(_projection).AsMaster();
            process.Register<TestEvent1>(_projection);
            process.Register<TestEvent2>(_projection);
            var task = process.Start();
            _eventStoreWaits.Wait(1000);
            process.Stop();
            task.GetAwaiter().GetResult();
            var expectedProjection = "1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n";
            var metadata = _metadata.GetInstanceMetadata("B").Result;
            var token = _metadata.GetToken("B").Result.ToString();
            Assert.IsFalse(_projection.WasRebuilt);
            Assert.AreEqual(expectedProjection, _projection.Contents.ToString());
            Assert.AreEqual("1.2", metadata.Version);
            Assert.AreEqual(ProjectionStatus.Running, metadata.Status);
            Assert.AreEqual("00000004", token);
        }

        [TestMethod]
        public void ContinueInitial_AsRebuilder()
        {
            AddEvent("TestEvent1", "e1");
            AddEvent("TestEvent2", "e2");
            AddEvent("TestEvent1", "e3");
            AddEventPause();
            AddEvent("TestEvent1", "e4");

            _metadata.SetToken("B", _events[1].Token);
            var originalContents = "1: e1\r\n2: e2\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild);
            _metadata.SetMetadata(originalMetadata);
            _projection.Contents.Append(originalContents);

            var process = new ProjectionProcess(_streamer, _metadataMgr.Object, new TestSerializer());
            process.Setup(_projection).AsRebuilder();
            process.Register<TestEvent1>(_projection);
            process.Register<TestEvent2>(_projection);
            var timeout = new Timer(o => process.Stop(), null, 1000, Timeout.Infinite);
            var task = process.Start();
            task.GetAwaiter().GetResult();
            timeout.Dispose();
            var metadata = _metadata.GetInstanceMetadata("B").Result;
            Assert.AreEqual(originalContents, _projection.Contents.ToString());
            Assert.AreEqual(originalMetadata, metadata);
        }

        private Task WaitForExit(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<object>();
            token.Register(() => tcs.SetCanceled());
            _eventStoreWaits.Set();
            return tcs.Task;
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

            public void Handle(TestEvent1 evt)
            {
                Contents.AppendFormat("1: {0}\r\n", evt.Data);
            }
            public void Handle(TestEvent2 evt)
            {
                Contents.AppendFormat("2: {0}\r\n", evt.Data);
            }

            public ProjectionRebuildType NeedsRebuild(string storedVersion)
            {
                return ProjectionRebuildType.Initial;
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

            public string GenerateInstanceName(string masterName)
            {
                if (masterName == null)
                    return "A";
                else
                    return abc.Substring(masterName[0] - 'A' + 1, 1);
            }

            public Task SetInstanceName(string instanceName)
            {
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
            private IEnumerable<EventStoreEvent> _events;
            private Func<CancellationToken, Task> _waitForExit;
            public TestStreamer(IEnumerable<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit)
            {
                _events = events ?? new EventStoreEvent[0];
                _waitForExit = waitForExit ?? (c => TaskResult.GetCompletedTask());
            }
            public IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode)
            {
                var typeNames = new HashSet<string>(filter.Select(t => t.Name));
                return new TestStream(_events.GetEnumerator(), _waitForExit, typeNames, token);
            }
        }

        private class TestStream : IEventStreamingInstance
        {
            private IEnumerator<EventStoreEvent> _events;
            private Func<CancellationToken, Task> _waitForExit;
            private HashSet<string> _types;
            private EventStoreToken _token;
            private bool _wasTokenFound;
            public TestStream(IEnumerator<EventStoreEvent> events, Func<CancellationToken, Task> waitForExit, HashSet<string> types, EventStoreToken token)
            {
                _events = events;
                _waitForExit = waitForExit;
                _types = types;
                _token = token;
                _wasTokenFound = token == EventStoreToken.Initial;
            }
            public Task<EventStoreEvent> GetNextEvent(CancellationToken cancel)
            {
                while (_events.MoveNext())
                {
                    cancel.ThrowIfCancellationRequested();
                    if (_events.Current == null)
                        return TaskResult.GetCompletedTask(_events.Current);
                    if (!_wasTokenFound)
                    {
                        if (_token == _events.Current.Token)
                            _wasTokenFound = true;
                        continue;
                    }
                    if (!_types.Contains(_events.Current.Type))
                        continue;
                    return TaskResult.GetCompletedTask(_events.Current);
                }
                return _waitForExit(cancel).ContinueWith<EventStoreEvent>(t => { t.GetAwaiter().GetResult(); return null; });

            }
        }

        private class TestMetadata : IProjectionMetadata
        {
            private Dictionary<string, ProjectionInstanceMetadata> _metadata;
            private Dictionary<string, EventStoreToken> _tokens = new Dictionary<string, EventStoreToken>();
            private List<Tuple<string, IHandle<ProjectionMetadataChanged>>> _changes = new List<Tuple<string, IHandle<ProjectionMetadataChanged>>>();

            public TestMetadata(List<ProjectionInstanceMetadata> metadata)
            {
                _metadata = metadata.ToDictionary(m => m.Name);
            }

            public Task BuildNewInstance(string instanceName, string nodeName, string version, string minimalReader)
            {
                _metadata[instanceName] = new ProjectionInstanceMetadata(instanceName, version, minimalReader, null, ProjectionStatus.NewBuild);
                return TaskResult.GetCompletedTask();
            }

            public Task Upgrade(string instanceName, string newVersion)
            {
                var old = _metadata[instanceName];
                var newMetadata = new ProjectionInstanceMetadata(old.Name, newVersion, old.MinimalReader, null, old.Status);
                _metadata[instanceName] = newMetadata;
                RaiseChanges(new ProjectionMetadataChanged(newMetadata, null));
                return TaskResult.GetCompletedTask();
            }

            public Task UpdateStatus(string instanceName, ProjectionStatus status)
            {
                var old = _metadata[instanceName];
                var newMetadata = new ProjectionInstanceMetadata(old.Name, old.Version, old.MinimalReader, null, status);
                _metadata[instanceName] = newMetadata;
                return TaskResult.GetCompletedTask();
            }

            private void RaiseChanges(ProjectionMetadataChanged evt)
            {
                var handlers = _changes.Where(t => t.Item1 == evt.InstanceName).Select(i => i.Item2).ToList();
                foreach (var handler in handlers)
                    handler.Handle(evt);
            }

            public Task<EventStoreToken> GetToken(string instanceName)
            {
                return TaskResult.GetCompletedTask(_tokens.Where(t => t.Key == instanceName).Select(t => t.Value).FirstOrDefault());
            }

            public Task SetToken(string instanceName, EventStoreToken token)
            {
                _tokens[instanceName] = token;
                return TaskResult.GetCompletedTask();
            }

            public Task<IEnumerable<ProjectionInstanceMetadata>> GetAllMetadata()
            {
                return TaskResult.GetCompletedTask(_metadata.Values.AsEnumerable());
            }

            public Task<ProjectionInstanceMetadata> GetInstanceMetadata(string instanceName)
            {
                return TaskResult.GetCompletedTask(_metadata.Values.FirstOrDefault(m => m.Name == instanceName));
            }

            public IDisposable RegisterForChanges(string instanceName, IHandle<ProjectionMetadataChanged> handler)
            {
                var tuple = new Tuple<string, IHandle<ProjectionMetadataChanged>>(instanceName, handler);
                _changes.Add(tuple);
                return new UnregisterMetadataChanges { metadata = this, tuple = tuple };
            }

            private class UnregisterMetadataChanges : IDisposable
            {
                public TestMetadata metadata;
                public Tuple<string, IHandle<ProjectionMetadataChanged>> tuple;
                public void Dispose() { metadata._changes.Remove(tuple); }
            }

            public void SetMetadata(ProjectionInstanceMetadata metadata)
            {
                _metadata[metadata.Name] = metadata;
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
