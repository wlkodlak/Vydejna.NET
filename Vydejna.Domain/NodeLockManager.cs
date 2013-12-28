﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface INodeLockManager
    {
        IDisposable Lock(string lockName, Action onLocked, Action cannotLock, bool nowait);
        void Unlock(string lockName);
        void Dispose();
    }

    public class NodeLockManager : INodeLockManager
    {
        private IDocumentFolder _store;
        private string _nodeName;
        private object _lock;
        private HashSet<string> _ownedLocks;
        private List<IDisposable> _lockers;

        public NodeLockManager(IDocumentFolder store, string nodeName)
        {
            _store = store;
            _nodeName = nodeName;
            _lock = new object();
            _ownedLocks = new HashSet<string>();
            _lockers = new List<IDisposable>();
        }

        public IDisposable Lock(string lockName, Action onLocked, Action cannotLock, bool nowait)
        {
            var locker = new LockExecutor(this, lockName, onLocked, cannotLock, nowait);
            locker.Execute();
            return locker;
        }

        private class LockExecutor : IDisposable
        {
            private NodeLockManager _parent;
            private string _lockName;
            private Action _onLocked;
            private Action _cannotLock;
            private bool _nowait;
            private bool _cancel;
            private bool _busy;
            private bool _pendingChange;
            private IDisposable _documentWatch;

            public LockExecutor(NodeLockManager parent, string lockName, Action onLocked, Action cannotLock, bool nowait)
            {
                _parent = parent;
                _lockName = lockName;
                _onLocked = onLocked;
                _cannotLock = cannotLock;
                _nowait = nowait;
            }
            public void Execute()
            {
                _documentWatch = _parent._store.WatchChanges(_lockName, OnLockChanged);
                OnLockChanged();
            }
            private void OnLockChanged()
            {
                lock (_parent._lock)
                {
                    if (_cancel)
                        return;
                    if (_busy)
                    {
                        _pendingChange = true;
                        return;
                    }
                    else
                    {
                        _pendingChange = false;
                        _busy = true;
                    }
                }
                _parent._store.GetDocument(_lockName, OnLockFound, OnLockMissing, OnError);
            }
            private void OnLockFound(int version, string owningNode)
            {
                bool cancel;
                lock (_parent._lock)
                {
                    cancel = _cancel;
                    if (_cancel)
                        _busy = false;
                }
                if (cancel)
                    _cannotLock();
                else if (string.IsNullOrEmpty(owningNode))
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
            private void OnLockBusy()
            {
                bool tryAgain;
                bool reportCannotLock;
                lock (_parent._lock)
                {
                    tryAgain = _pendingChange && !_cancel && !_nowait;
                    _pendingChange = false;
                    if (!tryAgain)
                        _busy = false;
                    reportCannotLock = _nowait;
                }
                if (tryAgain)
                    _parent._store.GetDocument(_lockName, OnLockFound, OnLockMissing, OnError);
                else if (reportCannotLock)
                    _cannotLock();
            }
            private void OnError(Exception exception)
            {
                lock (_parent._lock)
                {
                    _busy = false;
                    if (!_cancel)
                    {
                        _cancel = true;
                        _documentWatch.Dispose();
                    }
                }
                _cannotLock();
            }
            private void OnLockObtained()
            {
                lock (_parent._lock)
                {
                    _parent._ownedLocks.Add(_lockName);
                    if (!_cancel)
                    {
                        _cancel = true;
                        _documentWatch.Dispose();
                    }
                }
                _onLocked();
            }
            public void Dispose()
            {
                lock (_parent._lock)
                {
                    if (_cancel)
                        return;
                    _cancel = true;
                    _documentWatch.Dispose();
                }
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
                foreach (var locker in _lockers)
                    locker.Dispose();
                _lockers.Clear();
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