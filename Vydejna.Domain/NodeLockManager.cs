using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface INodeLockManager
    {
        void WaitForLock(string lockName, Action onLockChanged);
        void Lock(string lockName, Action<bool> onCompleted);
        void Unlock(string lockName);
        void Dispose();
    }
    public class NodeLockManager : INodeLockManager
    {
        private IDocumentFolder _store;
        private string _nodeName;
        private object _lock;
        private HashSet<string> _ownedLocks;
        private Dictionary<string, LockWatch> _allWatchers;

        private class LockWatch
        {
            private NodeLockManager _parent;
            private string _lockName;
            private List<Action> _watchers;
            private IDisposable _documentWatcher;

            public LockWatch(NodeLockManager parent, string lockName)
            {
                _parent = parent;
                _lockName = lockName;
                _watchers = new List<Action>();
            }

            public void AddWatcher(Action onLockChanged)
            {
                lock (_parent._lock)
                    _watchers.Add(onLockChanged);
                if (_documentWatcher == null)
                    _documentWatcher = _parent._store.WatchChanges(_lockName, LockDocumentChanged);
                LockDocumentChanged();
            }

            private void LockDocumentChanged()
            {
                lock (_parent._lock)
                {
                    if (_watchers.Count == 0)
                        return;
                }
                _parent._store.GetDocument(_lockName, LockDocumentLoaded, () => LockDocumentLoaded(0, null), ex => { });
            }

            private void LockDocumentLoaded(int version, string owner)
            {
                var isAvailable = string.IsNullOrEmpty(owner) || string.Equals(owner, _parent._nodeName);
                if (!isAvailable)
                    return;
                List<Action> watchers;
                lock (_parent._lock)
                {
                    watchers = _watchers.ToList();
                    _watchers.Clear();
                }
                foreach (var watcher in watchers)
                {
                    try
                    {
                        watcher();
                    }
                    catch
                    {
                    }
                }
            }

            public void Dispose()
            {
                if (_documentWatcher != null)
                    _documentWatcher.Dispose();
                _documentWatcher = null;
            }
        }

        public NodeLockManager(IDocumentFolder store, string nodeName)
        {
            _store = store;
            _nodeName = nodeName;
            _lock = new object();
            _ownedLocks = new HashSet<string>();
            _allWatchers = new Dictionary<string, LockWatch>();
        }

        public void WaitForLock(string lockName, Action onLockChanged)
        {
            lock (_lock)
            {
                LockWatch watchers;
                if (!_allWatchers.TryGetValue(lockName, out watchers))
                    _allWatchers[lockName] = watchers = new LockWatch(this, lockName);
                watchers.AddWatcher(onLockChanged);
            }
        }

        public void Lock(string lockName, Action<bool> onCompleted)
        {
            new LockExecutor(this, lockName, onCompleted).Execute();
        }

        private class LockExecutor
        {
            private NodeLockManager _parent;
            private string _lockName;
            private Action<bool> _onCompleted;
            public LockExecutor(NodeLockManager parent, string lockName, Action<bool> onCompleted)
            {
                _parent = parent;
                _lockName = lockName;
                _onCompleted = onCompleted;
            }
            public void Execute()
            {
                _parent._store.GetDocument(_lockName, OnLockFound, OnLockMissing, OnError);
            }
            private void OnLockFound(int version, string owningNode)
            {
                if (string.IsNullOrEmpty(owningNode))
                    _parent._store.SaveDocument(_lockName, _parent._nodeName, DocumentStoreVersion.At(version), OnLockObtained, OnLockBusy, OnError);
                else if (owningNode == _parent._nodeName)
                    OnLockObtained();
                else
                    OnLockBusy();
            }
            private void OnLockMissing()
            {
                _parent._store.SaveDocument(_lockName, _parent._nodeName, DocumentStoreVersion.New, OnLockObtained, OnLockBusy, OnError);
            }
            private void OnError(Exception exception)
            {
                _onCompleted(false);
            }
            private void OnLockBusy()
            {
                _onCompleted(false);
            }
            private void OnLockObtained()
            {
                lock (_parent._lock)
                    _parent._ownedLocks.Add(_lockName);
                _onCompleted(true);
            }
        }

        public void Unlock(string lockName)
        {
            _store.SaveDocument(lockName, "", DocumentStoreVersion.Any, NoAction, NoAction, IgnoreError);
            lock (_lock)
                _ownedLocks.Remove(lockName);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var watcher in _allWatchers.Values)
                    watcher.Dispose();
                _allWatchers.Clear();
                foreach (var lockName in _ownedLocks)
                    _store.SaveDocument(lockName, "", DocumentStoreVersion.Any, NoAction, NoAction, IgnoreError);
                _ownedLocks.Clear();
            }
        }
        private void NoAction()
        {
        }
        private void IgnoreError(Exception exception)
        {
        }
    }
}
