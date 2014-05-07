using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class NodeLockManagerTests
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private NodeLockManagerDocument _mgr;
        private VirtualTime _time;
        private NotifyChangeDirect _notify;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2013, 12, 22, 18, 22, 14));
            _notify = new NotifyChangeDirect(_executor);
            _mgr = new NodeLockManagerDocument(_folder, "node1", _time, _notify);
        }

        [TestMethod]
        public void LockAvailable()
        {
            string lockResult = null;

            _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", false);
            _executor.Process();

            Assert.AreEqual("owned", lockResult, "LockResult");
            Assert.AreEqual("2013-12-22 18:23:14;node1", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsImmediatellyWhenNowait()
        {
            _folder.SaveDocument("testlock", "2013-12-22 18:23:02;node2");
            string lockResult = null;

            _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", true);
            _executor.Process();

            Assert.AreEqual("blocked", lockResult);
            Assert.AreEqual("2013-12-22 18:23:02;node2", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsOnDispose()
        {
            _folder.SaveDocument("testlock", "2013-12-22 18:23:02;node2");
            string lockResult = null;

            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", false);
            _executor.Process();
            Assert.AreEqual(null, lockResult, "Should wait");

            stopWait.Dispose();
            _executor.Process();
            Assert.AreEqual("blocked", lockResult);
            Assert.AreEqual("2013-12-22 18:23:02;node2", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void WaitingEndsOnSuccess()
        {
            _folder.SaveDocument("testlock", "2013-12-22 18:23:02;node2");
            string lockResult = null;
            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", false);
            _executor.Process();
            Assert.AreEqual(null, lockResult, "Should wait");

            _folder.SaveDocument("testlock", "");
            _time.SetTime(new DateTime(2013, 12, 22, 18, 22, 44));
            _executor.Process();

            Assert.AreEqual("owned", lockResult);
            Assert.AreEqual("2013-12-22 18:23:44;node1", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void UnlockAtTheEnd()
        {
            string lockResult = null;
            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", false);
            _executor.Process();

            _mgr.Unlock("testlock");
            _executor.Process();

            Assert.AreEqual("owned", lockResult);
            Assert.AreEqual("", _folder.GetDocument("testlock"), "Contents");
        }

        [TestMethod]
        public void RelockEvery30Seconds()
        {
            string lockResult = null;
            var stopWait = _mgr.Lock("testlock", () => lockResult = "owned", ex => lockResult = "blocked", false);
            _executor.Process();

            _time.SetTime(new DateTime(2013, 12, 22, 18, 22, 45));
            _executor.Process();

            Assert.AreEqual("owned", lockResult);
            Assert.AreEqual("2013-12-22 18:23:45;node1", _folder.GetDocument("testlock"), "Contents");
        }
    }
}
