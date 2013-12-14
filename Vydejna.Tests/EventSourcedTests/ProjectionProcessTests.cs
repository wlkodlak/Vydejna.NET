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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [TestClass]
    public class ProjectionProcessTests : EventStreamingTestBase
    {
        private TestProjection _projection;
        private ProjectionProcess _process;
        private Task _processTask;

        protected TestProjection Projection { get { return _projection; } }

        [TestInitialize]
        public override void Initialize()
        {
            base.Initialize();
            _projection = new TestProjection();
        }

        protected override string ConsumerNameForMetadata()
        {
            return "TestProjection";
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
            ProjectionMetadata.SetToken("B", Events[1].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n");
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

            var originalToken = Events[1].Token;
            var originalContents = "1: e1\r\n2: e2\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild);
            ProjectionMetadata.SetToken("B", originalToken);
            ProjectionMetadata.SetMetadata(originalMetadata);
            Projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void RebuildInitial_AsMaster()
        {
            SetupEventStore();
            ProjectionMetadata.SetToken("B", Events[1].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.0", "1.0", null, ProjectionStatus.NewBuild));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n");
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

            var originalToken = Events[1].Token;
            var originalContents = "1: e1\r\n2: e2\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.0", "1.0", null, ProjectionStatus.NewBuild);
            ProjectionMetadata.SetToken("B", originalToken);
            ProjectionMetadata.SetMetadata(originalMetadata);
            Projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void ContinueRunning_AsMaster()
        {
            SetupEventStore();
            ProjectionMetadata.SetToken("B", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");
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

            var originalToken = Events[3].Token;
            var originalContents = "1: e1\r\n2: e2\r\n1: e3\r\n";
            var originalMetadata = new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running);
            ProjectionMetadata.SetToken("B", originalToken);
            ProjectionMetadata.SetMetadata(originalMetadata);
            Projection.Contents.Append(originalContents);

            RunAsIdleRebuilder();
            ExpectMetadata(originalMetadata);
            ExpectContents(originalContents);
            ExpectToken("B", originalToken);
        }

        [TestMethod]
        public void RebuildRunningOnly_AsMaster()
        {
            SetupEventStore();

            ProjectionMetadata.SetToken("B", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");

            RunAsMasterContinuable();
            ProjectionMetadata.UpdateStatus("B", ProjectionStatus.Legacy);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.3", "1.3", null, ProjectionStatus.Running));
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsMasterFinish();

            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Legacy));
            ExpectToken("B", Events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
            ExpectInstance("B");
        }

        [TestMethod]
        public void ParallelRebuild_AsMaster()
        {
            SetupEventStore();

            ProjectionMetadata.SetToken("B", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Running));
            ProjectionMetadata.SetToken("C", Events[1].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.3", "1.3", null, ProjectionStatus.NewBuild));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n1: e3\r\n");

            RunAsMasterContinuable();
            ProjectionMetadata.UpdateStatus("B", ProjectionStatus.Legacy);
            ProjectionMetadata.UpdateStatus("C", ProjectionStatus.Running);
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsMasterFinish();

            ExpectRebuild(ProjectionRebuildType.NoRebuild);
            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.Legacy));
            ExpectToken("B", Events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
            ExpectInstance("B");
        }

        [TestMethod]
        public void RebuildRunningOnly_AsRebuilder()
        {
            SetupEventStore();

            ProjectionMetadata.SetToken("C", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));

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

            ProjectionMetadata.SetToken("C", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));
            ProjectionMetadata.SetToken("D", Events[1].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.NewBuild));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n");

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

            ProjectionMetadata.SetToken("C", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.0", "1.0", null, ProjectionStatus.Running));
            ProjectionMetadata.SetToken("D", Events[1].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("D", "1.1", "1.0", null, ProjectionStatus.NewBuild));
            Projection.Contents.Append("1: e1\r\n2: e2\r\n");

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

            ProjectionMetadata.SetToken("C", Events[3].Token);
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("C", "1.1", "1.0", null, ProjectionStatus.Running));

            RunAsRebuilderContinuable();
            ProjectionMetadata.SetMetadata(new ProjectionInstanceMetadata("E", "1.3", "1.3", null, ProjectionStatus.NewBuild));
            ProjectionMetadata.UpdateStatus("D", ProjectionStatus.CancelledBuild);
            AddEvent("TestEvent2", "e5");
            SignalMoreEvents();
            RunAsRebuilderFinish();

            ExpectContents("1: e1\r\n2: e2\r\n1: e3\r\n1: e4\r\n");
            ExpectMetadata(new ProjectionInstanceMetadata("D", "1.2", "1.0", null, ProjectionStatus.CancelledBuild));
            ExpectToken("D", Events.Where(e => e != null && e.Body == "e4").Select(e => e.Token).Single());
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

        protected void ExpectLastToken(string instanceName)
        {
            var actualToken = ProjectionMetadata.GetToken(instanceName).Result.ToString();
            var expectedToken = Events.Where(e => e != null && e.Type != "TestEventX").Select(e => e.Token.ToString()).LastOrDefault();
            Assert.AreEqual(expectedToken, actualToken, "Token for {0}", instanceName);
        }

        protected void ExpectToken(string instanceName, EventStoreToken token)
        {
            var actualToken = ProjectionMetadata.GetToken(instanceName).Result;
            if (token == null)
                Assert.IsNull(actualToken, "Token for {0}", instanceName);
            else if (actualToken == null)
                Assert.AreEqual(token.ToString(), null, "Token for {0}", instanceName);
            else
                Assert.AreEqual(token.ToString(), actualToken.ToString(), "Token for {0}", instanceName);
        }

        protected void ExpectMetadata(ProjectionInstanceMetadata expectedMetadata)
        {
            var actualMetadata = ProjectionMetadata.GetInstanceMetadata(expectedMetadata.Name).Result;
            Assert.AreEqual(expectedMetadata, actualMetadata, "Metadata for {0}", expectedMetadata.Name);
        }

        protected void ExpectNoMetadata(string instanceName)
        {
            var actualMetadata = ProjectionMetadata.GetInstanceMetadata(instanceName).Result;
            Assert.IsNull(actualMetadata, "Metadata for {0} should not exist", instanceName);
        }

        private void ExpectContents(string expectedProjection)
        {
            Assert.AreEqual(expectedProjection, Projection.Contents.ToString(), "Contents");
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
            Assert.AreEqual(expectedWasRebuilt, Projection.WasRebuilt, "Was rebuilt");
            Assert.AreEqual(expectedRebuildContinued, Projection.WasInRebuildMode, "Rebuild started or continued");
        }

        private void ExpectInstance(string instanceName)
        {
            Assert.AreEqual(instanceName, Projection.InstanceName, "Instance name");
        }

        private void RunAsMaster()
        {
            RunAsMasterContinuable();
            RunAsMasterFinish();
        }

        private void RunAsMasterContinuable()
        {
            _process = new ProjectionProcess(Streamer, MetadataMgr, new TestSerializer());
            _process.Setup(Projection).AsMaster();
            _process.Register<TestEvent1>(Projection);
            _process.Register<TestEvent2>(Projection);
            _processTask = _process.Start();
            _processTask.ContinueWith(t => EventStoreWaits.Set(), TaskContinuationOptions.OnlyOnFaulted);
            EventStoreWaits.Wait(1000);
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
                MetadataMgr, new TestSerializer());
            process.Setup(Projection).AsRebuilder();
            process.Register<TestEvent1>(Projection);
            process.Register<TestEvent2>(Projection);
            var timeout = new Timer(o => process.Stop(), null, 1000, Timeout.Infinite);
            var task = process.Start();
            task.GetAwaiter().GetResult();
            timeout.Dispose();
        }

        private void RunAsRebuilderContinuable()
        {
            _process = new ProjectionProcess(Streamer, MetadataMgr, new TestSerializer());
            _process.Setup(Projection).AsRebuilder();
            _process.Register<TestEvent1>(Projection);
            _process.Register<TestEvent2>(Projection);
            _processTask = _process.Start();
            _processTask.ContinueWith(t => EventStoreWaits.Set(), TaskContinuationOptions.OnlyOnFaulted);
            EventStoreWaits.Wait(1000);
        }

        private void RunAsRebuilderFinish()
        {
            _process.Stop();
            _processTask.GetAwaiter().GetResult();
        }


        protected class TestProjection : IProjection, IHandle<TestEvent1>, IHandle<TestEvent2>
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

        protected class TestEvent1
        {
            public string Data;
        }
        protected class TestEvent2
        {
            public string Data;
        }

        protected class TestSerializer : IEventSourcedSerializer
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
