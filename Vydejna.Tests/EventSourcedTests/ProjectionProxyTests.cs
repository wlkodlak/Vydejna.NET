using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Vydejna.Contracts;
using Vydejna.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public class ProjectionProxyTests
    {
        private List<ProjectionInstanceMetadata> _metadataList;
        private TestMetadata _metadata;
        private Mock<IProjectionMetadataManager> _metadataMgr;
        private TestReader _reader1;
        private TestReader _reader2;
        private TestProxy _proxy;

        [TestInitialize]
        public void Initialize()
        {
            _metadataList = new List<ProjectionInstanceMetadata>();
            _metadata = new TestMetadata(_metadataList);
            _metadataMgr = new Mock<IProjectionMetadataManager>();
            _metadataMgr.Setup(m => m.GetProjection("TestProjection")).Returns(() => TaskResult.GetCompletedTask<IProjectionMetadata>(_metadata));
            _reader1 = new TestReader("X");
            _reader2 = new TestReader("Y");
        }

        [TestMethod]
        public void SetupDoesNotCrash()
        {
            _proxy = new TestProxy(_metadataMgr.Object);
            _proxy.Register(_reader1);
            _proxy.Register(_reader2);
            _proxy.InitializeInstances().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void SingleSupportedRunning()
        {
            SetupInstance("A", "1.0", "1.0", ProjectionStatus.Running);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "A");
        }

        [TestMethod]
        public void SingleSupportedNewBuild()
        {
            SetupInstance("A", "1.0", "1.0", ProjectionStatus.NewBuild);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "A");
        }

        [TestMethod]
        public void SingleSupportedLegacy()
        {
            SetupInstance("A", "1.0", "1.0", ProjectionStatus.Legacy);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "A");
        }

        [TestMethod]
        public void RunningNewOld()
        {
            SetupInstance("A", "1.0", "1.0", ProjectionStatus.Discontinued);
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.0", ProjectionStatus.NewBuild);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "B");
        }

        [TestMethod]
        public void RunningRunningOld()
        {
            SetupInstance("A", "1.0", "1.0", ProjectionStatus.Discontinued);
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.0", ProjectionStatus.Running);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "C");
        }

        [TestMethod]
        public void UpdateNewToRunning()
        {
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.0", ProjectionStatus.NewBuild);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            UpdateInstance("C", ProjectionStatus.Running);
            VerifyReader(_reader1, "C");
        }

        [TestMethod]
        public void UpdateRunningToLegacy()
        {
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.0", ProjectionStatus.Running);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            UpdateInstance("B", ProjectionStatus.Legacy);
            VerifyReader(_reader1, "C");
        }

        [TestMethod]
        public void InstanceTooNewForReader()
        {
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.2", ProjectionStatus.Running);
            SetupReader(_reader1, "1.0", "1.0");
            SetupProxy(_reader1);
            VerifyReader(_reader1, "B");
        }

        [TestMethod]
        public void MultipleReadersBeforeUpdate()
        {
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.2", ProjectionStatus.NewBuild);
            SetupReader(_reader1, "1.0", "1.0");
            SetupReader(_reader2, "1.2", "1.2");
            SetupProxy(_reader1, _reader2);
            VerifyReader(_reader1, "B");
        }

        [TestMethod]
        public void MultipleReadersAfterUpdate()
        {
            SetupInstance("B", "1.1", "1.0", ProjectionStatus.Running);
            SetupInstance("C", "1.2", "1.2", ProjectionStatus.NewBuild);
            SetupReader(_reader1, "1.0", "1.0");
            SetupReader(_reader2, "1.2", "1.2");
            SetupProxy(_reader1, _reader2);
            UpdateInstance("C", ProjectionStatus.Running);
            VerifyReader(_reader2, "C");
        }


        private void SetupInstance(string name, string version, string minReader, ProjectionStatus status)
        {
            _metadata.BuildNewInstance(name, null, version, minReader);
            _metadata.UpdateStatus(name, status);
        }
        private void UpdateInstance(string name, ProjectionStatus status)
        {
            _metadata.UpdateStatus(name, status);
        }
        private void SetupReader(TestReader reader, string version, string minVersion)
        {
            reader.Version = version;
            reader.MinimalStoredVersion = version;
        }
        private void SetupProxy(params TestReader[] readers)
        {
            _proxy = new TestProxy(_metadataMgr.Object);
            foreach (var reader in readers)
                _proxy.Register(reader);
            _proxy.InitializeInstances().GetAwaiter().GetResult();
        }
        private void VerifyReader(TestReader expectedReader, string expectedInstance)
        {
            Assert.AreEqual(
                expectedReader == null ? null : expectedReader.Name, 
                _proxy.Reader == null ? null : _proxy.Reader.Name, 
                "Reader");
            if (expectedReader != null)
                Assert.AreEqual(expectedInstance, expectedReader.CurrentInstance, "Instance for {0}", expectedReader.Name);
        }


        private class TestProxy : ProjectionProxy<TestReader>
        {
            public TestProxy(IProjectionMetadataManager metadataMgr)
                : base(metadataMgr, "TestProjection")
            {
            }

            public new TestReader Reader { get { return base.Reader; } }
        }

        private class TestReader : IProjectionReader
        {
            public string Name;
            public string Version;
            public string CurrentInstance;
            public string MinimalStoredVersion;

            public TestReader(string name)
            {
                Name = name;
            }

            public string GetVersion()
            {
                return Version;
            }

            public string GetProjectionName()
            {
                return "TestProjection";
            }

            public ProjectionReadability GetReadability(string minimalReaderVersion, string storedVersion)
            {
                return ProjectionUtils.CheckReaderVersion(minimalReaderVersion, GetVersion(), storedVersion, MinimalStoredVersion ?? "0.0");
            }

            public void UseInstance(string instanceName)
            {
                CurrentInstance = instanceName;
            }
        }
    }
}
