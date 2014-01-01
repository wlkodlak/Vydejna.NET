using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class NodeLockManagerTests
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private NodeLockManager _mgr;
        
        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _mgr = new NodeLockManager(_folder, "node1");
        }

        [TestMethod]
        public void LockAvailable()
        {
            string lockResult = null;
            
            _mgr.Lock("testlock", () => lockResult = "owned", () => lockResult = "blocked", false);
            _executor.Process();

            Assert.AreEqual("owned", lockResult, "LockResult");
            Assert.AreEqual("node1", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsImmediatellyWhenNowait()
        {
            _folder.SaveDocument("testlock", "node2");
            string lockResult = null;

            _mgr.Lock("testlock", () => lockResult = "owned", () => lockResult = "blocked", true);
            _executor.Process();

            Assert.AreEqual("blocked", lockResult);
            Assert.AreEqual("node2", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsOnDispose()
        {
            _folder.SaveDocument("testlock", "node2");
            string lockResult = null;

            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", () => lockResult = "blocked", false);
            _executor.Process();
            Assert.AreEqual(null, lockResult, "Should wait");

            stopWait.Dispose();
            _executor.Process();
            Assert.AreEqual("blocked", lockResult);
            Assert.AreEqual("node2", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsOnSuccess()
        {
            _folder.SaveDocument("testlock", "node2");
            string lockResult = null;
            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", () => lockResult = "blocked", false);
            _executor.Process();
            Assert.AreEqual(null, lockResult, "Should wait");

            _folder.SaveDocument("testlock", "");
            _executor.Process();

            Assert.AreEqual("owned", lockResult);
            Assert.AreEqual("node1", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void UnlockAtTheEnd()
        {
            string lockResult = null;
            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", () => lockResult = "blocked", false);
            _executor.Process();

            _mgr.Unlock("testlock");
            _executor.Process();

            Assert.AreEqual("owned", lockResult);
            Assert.AreEqual("", _folder.GetDocument("testlock"), "Contents");
        }
    }
}
