using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class NodeLockTests
    {
        private TestLockMgr _mgr;
        private NodeLock _lock;

        [TestInitialize]
        public void Initialize()
        {
            _mgr = new TestLockMgr();
            _lock = new NodeLock(_mgr, "testlock");
        }

        private class DelegatedDispose : IDisposable
        {
            private Action _dispose;
            public DelegatedDispose(Action dispose) { _dispose = dispose; }
            public void Dispose() { _dispose(); }
        }

        [TestMethod]
        public void ObtainLock()
        {
            string result = null;
            _lock.Lock(() => result = "got", () => result = "fail");
            Assert.IsNotNull(_mgr.OnLocked, "OnLocked null");
            _mgr.IsLocked = true;
            _mgr.OnLocked();
            Assert.IsTrue(_lock.IsLocked, "IsLocked");
            Assert.AreEqual("testlock", _mgr.LockName, "LockName");
            Assert.IsFalse(_mgr.Nowait, "Nowait");
            Assert.AreEqual("got", result, "Result");
        }

        [TestMethod]
        public void DisposeDoesNothingWhenLocked()
        {
            _lock.Lock(() => { }, () => { });
            Assert.IsNotNull(_mgr.OnLocked, "OnLocked null");
            _mgr.IsLocked = true;
            _mgr.OnLocked();
            _lock.Dispose();
            Assert.IsFalse(_mgr.IsDisposed, "IsDisposed");
            Assert.IsTrue(_lock.IsLocked, "IsLocked");
        }

        [TestMethod]
        public void WaitStopsOnDispose()
        {
            string result = null;
            _lock.Lock(() => result = "got", () => result = "fail");
            Assert.IsNotNull(_mgr.OnLocked, "OnLocked null");
            _lock.Dispose();
            Assert.AreEqual("testlock", _mgr.LockName, "LockName");
            Assert.IsFalse(_mgr.Nowait, "Nowait");
            Assert.IsTrue(_mgr.IsDisposed, "IsDisposed");
            Assert.AreEqual("fail", result, "Result");
            Assert.IsFalse(_lock.IsLocked, "IsLocked");
        }

        [TestMethod]
        public void UnlockRedirected()
        {
            _lock.Unlock();
            Assert.AreEqual("testlock", _mgr.UnlockCalled);
            Assert.IsFalse(_lock.IsLocked, "IsLocked");
        }

        private class TestLockMgr : INodeLockManager
        {
            public string LockName;
            public Action OnLocked, CannotLock;
            public bool Nowait;
            public string UnlockCalled;
            public bool IsDisposed;
            public bool IsLocked;

            public IDisposable Lock(string lockName, Action onLocked, Action cannotLock, bool nowait)
            {
                LockName = lockName;
                OnLocked = onLocked;
                CannotLock = cannotLock;
                Nowait = nowait;
                return new DelegatedDispose(() => { IsDisposed = true; CannotLock(); });
            }

            public void Unlock(string lockName)
            {
                UnlockCalled = lockName;
                IsLocked = false;
            }

            public void Dispose()
            {
            }

            bool INodeLockManager.IsLocked(string lockName)
            {
                return IsLocked;
            }
        }
    }
}
