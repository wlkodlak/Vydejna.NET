using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public class ProjectionMetadataTests_SingleManager
    {
        private DocumentStoreInMemory _store;
        private string _prefix = "GlobalMetadata";
        private ProjectionMetadataManager _mgr;
        private IProjectionMetadata _projection;

        [TestInitialize]
        public void Initialize()
        {
            _store = new DocumentStoreInMemory();
            _mgr = new ProjectionMetadataManager(_store, _prefix);
            _projection = _mgr.GetProjection("Projection").GetAwaiter().GetResult();
        }

        [TestMethod]
        public void GetMetadata_EmptyStore_ReturnsEmpty()
        {
            var allList = _projection.GetAllMetadata().GetAwaiter().GetResult();
            Assert.IsNotNull(allList, "No result");
            Assert.AreEqual(0, allList.Count(), "Count");
        }

        [TestMethod]
        public void BuildNewInstance_EmptyStore_AddsInstanceToProjection()
        {
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            var allList = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            Assert.AreEqual(1, allList.Count, "Count");
            var expectedMetadata = new ProjectionInstanceMetadata("A", "1.0", "1.0", null, ProjectionStatus.NewBuild);
            Assert.AreEqual(expectedMetadata, allList[0], "Metadata");
        }

        [TestMethod]
        public void UpdateStatus_ExistingInstance_ChangesStatus()
        {
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("A", ProjectionStatus.Running);
            var allList = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            Assert.AreEqual(1, allList.Count, "Count");
            var expectedMetadata = new ProjectionInstanceMetadata("A", "1.0", "1.0", null, ProjectionStatus.Running);
            Assert.AreEqual(expectedMetadata, allList[0], "Metadata");
        }

        private class HandleNotifications : IHandle<ProjectionMetadataChanged>
        {
            public Action<ProjectionMetadataChanged> _handler;
            public HandleNotifications(Action<ProjectionMetadataChanged> handler)
            {
                _handler = handler;
            }
            public void Handle(ProjectionMetadataChanged message)
            {
                _handler(message);
            }
        }

        [TestMethod]
        public void UpdateStatus_ExistingInstance_NotifiesInstance()
        {
            var notifiedStatusNamed = ProjectionStatus.NewBuild;
            var notifyHandlerNamed = new HandleNotifications(m => notifiedStatusNamed = m.Status);
            var notifiedStatusUnnamed = ProjectionStatus.NewBuild;
            var notifyHandlerUnnamed = new HandleNotifications(m => notifiedStatusUnnamed = m.Status);
            _projection.RegisterForChanges("A", notifyHandlerNamed);
            _projection.RegisterForChanges(null, notifyHandlerUnnamed);
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("A", ProjectionStatus.Running);
            var allList = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            Assert.AreEqual(ProjectionStatus.Running, notifiedStatusNamed, "Notified Named");
            Assert.AreEqual(ProjectionStatus.Running, notifiedStatusUnnamed, "Notified Unnamed");
        }

        [TestMethod]
        public void GetToken_InstanceNonexistent_ReturnsInitial()
        {
            var token = _projection.GetToken("X").GetAwaiter().GetResult();
            Assert.AreEqual(EventStoreToken.Initial, token);
        }

        [TestMethod]
        public void GetToken_InstanceCreated_ReturnsInitial()
        {
            _projection.BuildNewInstance("X", null, "1.0", "1.0").GetAwaiter().GetResult();
            var token = _projection.GetToken("X").GetAwaiter().GetResult();
            Assert.AreEqual(EventStoreToken.Initial, token);
        }

        [TestMethod]
        public void SetToken_InstanceExists_SetsToken()
        {
            _projection.BuildNewInstance("X", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.SetToken("X", new EventStoreToken("00110022"));
            var token = _projection.GetToken("X").GetAwaiter().GetResult();
            Assert.AreEqual(new EventStoreToken("00110022"), token);
        }

        [TestMethod]
        public void GetAllMetadata_MultipleInstances_SortedByVersionDescending()
        {
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("A", ProjectionStatus.Running);
            _projection.BuildNewInstance("C", null, "1.2", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("C", ProjectionStatus.Running);
            _projection.BuildNewInstance("B", null, "1.3", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("B", ProjectionStatus.Running);
            var allList = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            var names = string.Join(",", allList.Select(i => i.Name));
            Assert.AreEqual("B,C,A", names);
        }

        [TestMethod]
        public void DifferentProjectionsAreIndependent()
        {
            var projection2 = _mgr.GetProjection("Projection2").GetAwaiter().GetResult();
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("A", ProjectionStatus.Running);
            _projection.SetToken("A", new EventStoreToken("00110022"));
            _projection.BuildNewInstance("B", null, "1.2", "1.0").GetAwaiter().GetResult();
            projection2.BuildNewInstance("A", null, "1.1", "0.9").GetAwaiter().GetResult();
            projection2.SetToken("A", new EventStoreToken("00001111"));
            var meta1 = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            var meta2 = projection2.GetAllMetadata().GetAwaiter().GetResult().ToList();
            Assert.AreEqual(2, meta1.Count, "1:Count");
            Assert.AreEqual(1, meta2.Count, "2:Count");
            Assert.AreEqual(new ProjectionInstanceMetadata("B", "1.2", "1.0", null, ProjectionStatus.NewBuild), meta1[0], "1:[0]");
            Assert.AreEqual(new ProjectionInstanceMetadata("A", "1.0", "1.0", null, ProjectionStatus.Running), meta1[1], "1:[0]");
            Assert.AreEqual(new EventStoreToken("00110022"), _projection.GetToken("A").Result, "1:[A].Token");
            Assert.AreEqual(new ProjectionInstanceMetadata("A", "1.1", "0.9", null, ProjectionStatus.NewBuild), meta2[0], "2:[0]");
            Assert.AreEqual(new EventStoreToken("00001111"), projection2.GetToken("A").Result, "1:[A].Token");
        }
        
        [TestMethod]
        public void SameProjectionsAreSynchronized()
        {
            var projection2 = _mgr.GetProjection("Projection").GetAwaiter().GetResult();
            _projection.BuildNewInstance("A", null, "1.0", "1.0").GetAwaiter().GetResult();
            _projection.UpdateStatus("A", ProjectionStatus.Running);
            _projection.SetToken("A", new EventStoreToken("00110022"));
            _projection.BuildNewInstance("B", null, "1.2", "1.0").GetAwaiter().GetResult();
            projection2.BuildNewInstance("A", null, "1.3", "1.0").GetAwaiter().GetResult();
            projection2.SetToken("A", new EventStoreToken("00001111"));
            var meta1 = _projection.GetAllMetadata().GetAwaiter().GetResult().ToList();
            var meta2 = projection2.GetAllMetadata().GetAwaiter().GetResult().ToList();

            Assert.AreEqual(meta1.Count, meta2.Count, "Count");
            for (int i = 0; i < meta1.Count; i++)
                Assert.AreEqual(meta1[i], meta2[i], "[{0}]", i);
        }

        [TestMethod]
        public void HandlerToken_Nonexistent()
        {
            var handler = _mgr.GetHandler("TestHandler").GetAwaiter().GetResult();
            var token = handler.GetToken();
            Assert.AreEqual(EventStoreToken.Initial, token);
        }
        [TestMethod]
        public void HandlerToken_SetToken()
        {
            var handler = _mgr.GetHandler("TestHandler").GetAwaiter().GetResult();
            handler.SetToken(new EventStoreToken("00031242")).GetAwaiter().GetResult();
            var token = handler.GetToken();
            Assert.AreEqual(new EventStoreToken("00031242"), token);
        }
    }

    [TestClass]
    public class ProjectionMetadataTests_Persistence
    {
        private DocumentStoreInMemory _store;

        [TestInitialize]
        public void Initialize()
        {
            _store = new DocumentStoreInMemory();
        }

        [TestMethod]
        public void ArePersistent()
        {
            ArePersistent_Setup().GetAwaiter().GetResult();
            ArePersistent_Verify().GetAwaiter().GetResult();
        }

        private async Task ArePersistent_Setup()
        {
            var mgr = new ProjectionMetadataManager(_store, "GlobalMetadata");
            var proj1 = await mgr.GetProjection("Projection");
            await proj1.BuildNewInstance("A", null, "1.1", "1.0");
            await proj1.UpdateStatus("A", ProjectionStatus.Running);
            await proj1.SetToken("A", new EventStoreToken("00001923"));
            var proj2 = await mgr.GetHandler("Handler");
            await proj2.SetToken(new EventStoreToken("00123320"));
        }
        private async Task ArePersistent_Verify()
        {
            var mgr = new ProjectionMetadataManager(_store, "GlobalMetadata");
            var proj1 = await mgr.GetProjection("Projection");
            var meta1 = (await proj1.GetAllMetadata()).ToList();
            Assert.AreEqual(1, meta1.Count, "Count");
            Assert.AreEqual(new ProjectionInstanceMetadata("A", "1.1", "1.0", null, ProjectionStatus.Running), meta1[0], "Proj.[0]");
            Assert.AreEqual(new EventStoreToken("00001923"), await proj1.GetToken("A"), "Proj.Token");
            var proj2 = await mgr.GetHandler("Handler");
            Assert.AreEqual(new EventStoreToken("00123320"), proj2.GetToken(), "Handler.Token");
        }
    }

}
