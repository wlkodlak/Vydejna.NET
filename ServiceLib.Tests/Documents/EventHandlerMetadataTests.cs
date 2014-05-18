using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class EventHandlerMetadataTests
    {
        private TestScheduler _scheduler;
        private TestDocumentFolder _folder;
        private Mock<INodeLockManager> _locking;
        private MetadataManager _mgr;
        private IMetadataInstance _inst;
        
        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _folder = new TestDocumentFolder();
            _locking = new Mock<INodeLockManager>();
            _mgr = new MetadataManager(_folder, _locking.Object);
            _inst = _mgr.GetConsumer("consumer");
        }

        [TestMethod]
        public void GetNonexistingToken()
        {
            var taskToken = _scheduler.Run(() => _inst.GetToken());
            Assert.IsTrue(taskToken.IsCompleted, "Complete");
            var token = taskToken.Result;
            Assert.AreEqual(EventStoreToken.Initial, token);
        }

        [TestMethod]
        public void GetExistingToken()
        {
            _folder.SaveDocumentSync("consumer_tok", "5584");
            var taskToken = _scheduler.Run(() => _inst.GetToken());
            Assert.IsTrue(taskToken.IsCompleted, "Complete");
            var token = taskToken.Result;
            Assert.AreEqual(new EventStoreToken("5584"), token);
        }

        [TestMethod]
        public void GetNonexistingVersion()
        {
            var taskVersion = _scheduler.Run(() => _inst.GetVersion());
            Assert.IsTrue(taskVersion.IsCompleted, "Complete");
            var version = taskVersion.Result;
            Assert.AreEqual("", version ?? "");
        }

        [TestMethod]
        public void GetExistingVersion()
        {
            _folder.SaveDocumentSync("consumer_ver", "1.12");
            var taskVersion = _scheduler.Run(() => _inst.GetVersion());
            Assert.IsTrue(taskVersion.IsCompleted, "Complete");
            var version = taskVersion.Result;
            Assert.AreEqual("1.12", version);
        }

        [TestMethod]
        public void LockMetadata()
        {
            var tcs = new TaskCompletionSource<object>();
            _locking
                .Setup(l => l.Lock("consumer", CancellationToken.None, false))
                .Returns(tcs.Task)
                .Verifiable();
            var taskLock = _scheduler.Run(() => _inst.Lock(CancellationToken.None), false);
            tcs.SetResult(null);
            _scheduler.Process();
            Assert.IsTrue(taskLock.IsCompleted, "Completed");
            Assert.IsNull(taskLock.Exception, "Exception");
        }

        [TestMethod]
        public void UnlockMetadata()
        {
            _locking.Setup(l => l.Unlock("consumer")).Verifiable();
            _inst.Unlock();
            _locking.Verify();
        }
    }
}
