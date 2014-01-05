using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using Moq;
using System;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class EventHandlerMetadataTests
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private Mock<INodeLockManager> _locking;
        private MetadataManager _mgr;
        private IMetadataInstance _inst;
        
        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _locking = new Mock<INodeLockManager>();
            _mgr = new MetadataManager(_folder, _locking.Object);
            _inst = _mgr.GetConsumer("consumer");
        }

        [TestMethod]
        public void GetNonexistingToken()
        {
            EventStoreToken token = null;
            _inst.GetToken(t => token = t, ThrowError);
            _executor.Process();
            Assert.AreEqual(EventStoreToken.Initial, token);
        }

        [TestMethod]
        public void GetExistingToken()
        {
            _folder.SaveDocument("consumer_tok", "5584");
            EventStoreToken token = null;
            _inst.GetToken(t => token = t, ThrowError);
            _executor.Process();
            Assert.AreEqual(new EventStoreToken("5584"), token);
        }

        [TestMethod]
        public void GetNonexistingVersion()
        {
            string version = null;
            _inst.GetVersion(ver => version = ver, ThrowError);
            _executor.Process();
            Assert.AreEqual("", version ?? "");
        }

        [TestMethod]
        public void GetExistingVersion()
        {
            _folder.SaveDocument("consumer_ver", "1.12");
            string version = null;
            _inst.GetVersion(ver => version = ver, ThrowError);
            _executor.Process();
            Assert.AreEqual("1.12", version);
        }

        private class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }

        [TestMethod]
        public void LockMetadata()
        {
            bool obtained;
            Action lockAction = null;
            var lockWait = new EmptyDisposable();
            _locking
                .Setup(l => l.Lock("consumer", It.IsAny<Action>(), It.IsAny<Action>(), false))
                .Callback<string, Action, Action, bool>((doc, succ, fail, nowait) => lockAction = succ)
                .Returns(lockWait);
            var returnedWait = _inst.Lock(() => obtained = true);
            Assert.IsNotNull(lockAction, "No success action");
            obtained = false;
            lockAction();
            Assert.IsTrue(obtained, "Correct action");
            Assert.AreSame(lockWait, returnedWait, "IDisposable");
        }

        [TestMethod]
        public void UnlockMetadata()
        {
            _locking.Setup(l => l.Unlock("consumer")).Verifiable();
            _inst.Unlock();
            _locking.Verify();
        }

        private void ThrowError(Exception ex)
        {
            throw ex;
        }
    }
}
