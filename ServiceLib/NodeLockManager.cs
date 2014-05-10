using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;

namespace ServiceLib
{
    public interface INodeLockManager
    {
        Task Lock(string lockName, CancellationToken cancel, bool nowait);
        bool IsLocked(string lockName);
        void Unlock(string lockName);
        void Dispose();
    }

    public class NodeLockManagerNull : INodeLockManager
    {
        private NullDispose _disposable = new NullDispose();
        private HashSet<string> _ownedLocks = new HashSet<string>();

        private class NullDispose : IDisposable
        {
            public void Dispose() { }
        }

        public Task Lock(string lockName, CancellationToken cancel, bool nowait)
        {
            lock (_ownedLocks)
            {
                _ownedLocks.Add(lockName);
                return TaskUtils.CompletedTask();
            }
        }

        public void Unlock(string lockName)
        {
            lock (_ownedLocks)
                _ownedLocks.Remove(lockName);
        }

        public void Dispose()
        {
            lock (_ownedLocks)
                _ownedLocks.Clear();
        }

        public bool IsLocked(string lockName)
        {
            lock (_ownedLocks)
                return _ownedLocks.Contains(lockName);
        }
    }
}
