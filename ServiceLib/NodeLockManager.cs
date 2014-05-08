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

    public interface INodeLock : IDisposable
    {
        void Lock(Action onLocked, Action<Exception> onError);
        void Unlock();
        bool IsLocked { get; }
    }

    public class NodeLockManagerDocument : INodeLockManager
    {
        private IDocumentFolder _store;
        private string _nodeName;
        private ITime _timeService;
        private object _lock;
        private Dictionary<string, OwnedLock> _ownedLocks;
        private List<LockExecutor> _lockers;
        private const int TimerInterval = 30000;
        private IDisposable _nextTimer;
        private INotifyChange _notificator;
        private IDisposable _listener;

        public NodeLockManagerDocument(IDocumentFolder store, string nodeName, ITime timer, INotifyChange notificator)
        {
            _store = store;
            _nodeName = nodeName;
            _timeService = timer;
            _notificator = notificator;
            _lock = new object();
            _ownedLocks = new Dictionary<string, OwnedLock>();
            _lockers = new List<LockExecutor>();
        }

        public IDisposable Lock(string lockName, Action onLocked, Action<Exception> onError, bool nowait)
        {
            var locker = new LockExecutor(this, lockName, onLocked, onError, nowait);
            lock (_lock)
            {
                if (_listener == null)
                    _listener = _notificator.Register(OnNotify);
                if (_nextTimer == null)
                    _nextTimer = _timeService.Delay(TimerInterval, OnTimer);
                _lockers.Add(locker);
            }
            locker.Execute();
            return locker;
        }

        private void OnNotify(string lockName, int version)
        {
            lock (_lock)
            {
                foreach (var lck in _lockers)
                    lck.Notify();
            }
        }

        public bool IsLocked(string lockName)
        {
            lock (_lock)
            {
                OwnedLock ownedLock;
                if (!_ownedLocks.TryGetValue(lockName, out ownedLock))
                    return false;
                return ownedLock.IsActive;
            }
        }

        private void OnTimer()
        {
            lock (_lock)
            {
                _nextTimer = _timeService.Delay(TimerInterval, OnTimer);
                foreach (var ownedLock in _ownedLocks.Values)
                    ownedLock.Update();
            }
        }

        private string ContentsToWrite()
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss};{1}",
                _timeService.GetUtcTime().AddMilliseconds(2 * TimerInterval),
                _nodeName);
        }

        private class OwnedLock
        {
            private NodeLockManagerDocument _parent;
            public string LockName;
            public int DocumentVersion;
            public DateTime LastWrite;
            public bool IsActive;
            private bool _isBusy;
            public bool IsDisposed;

            public OwnedLock(NodeLockManagerDocument parent, string lockName, int version)
            {
                _parent = parent;
                LockName = lockName;
                DocumentVersion = version;
                LastWrite = parent._timeService.GetUtcTime();
                IsActive = true;
            }

            public void Update()
            {
                if (_isBusy)
                    return;
                _isBusy = true;
                _parent._store.SaveDocument(
                    LockName, _parent.ContentsToWrite(), DocumentStoreVersion.At(DocumentVersion), null,
                    OnSaved, OnConcurrency, OnError);
            }

            private void OnSaved()
            {
                lock (_parent._lock)
                {
                    _isBusy = false;
                    LastWrite = _parent._timeService.GetUtcTime();
                    DocumentVersion++;
                    if (IsDisposed)
                    {
                        _isBusy = false;
                        IsActive = false;
                        _parent._ownedLocks.Remove(LockName);
                        _parent._store.SaveDocument(
                            LockName, "", DocumentStoreVersion.At(DocumentVersion), null,
                            Unlocked, Unlocked, OnError);
                    }
                }
            }

            private void OnConcurrency()
            {
                lock (_parent._lock)
                {
                    _isBusy = false;
                    IsActive = false;
                    _parent._ownedLocks.Remove(LockName);
                }
            }

            private void OnError(Exception obj)
            {
                lock (_parent._lock)
                {
                    _isBusy = false;
                    IsActive = false;
                    _parent._ownedLocks.Remove(LockName);
                }
            }

            public void Dispose()
            {
                lock (_parent._lock)
                {
                    IsDisposed = true;
                    if (!IsActive || _isBusy)
                        return;
                    IsActive = false;
                    _parent._store.SaveDocument(
                        LockName, "", DocumentStoreVersion.At(DocumentVersion), null,
                        Unlocked, Unlocked, OnError);
                }
            }

            private void Unlocked()
            {
                _parent._notificator.Notify(LockName, 0);
            }
        }

        private class LockExecutor : IDisposable
        {
            private NodeLockManagerDocument _parent;
            private string _lockName;
            private Action _onLocked;
            private Action<Exception> _onError;
            private bool _nowait;
            private bool _cancel;
            private int _savedVersion;
            private IDisposable _wait;

            public LockExecutor(NodeLockManagerDocument parent, string lockName, Action onLocked, Action<Exception> onError, bool nowait)
            {
                _parent = parent;
                _lockName = lockName;
                _onLocked = onLocked;
                _onError = onError;
                _nowait = nowait;
            }
            public void Execute()
            {
                OnLockChanged();
            }
            private void OnLockChanged()
            {
                lock (_parent._lock)
                {
                    if (_cancel)
                        return;
                }
                _parent._store.GetDocument(_lockName, OnLockFound, OnLockMissing, OnError);
            }
            private void OnLockFound(int version, string existingContents)
            {
                bool cancel;
                lock (_parent._lock)
                {
                    _savedVersion = version + 1;
                    cancel = _cancel;
                }
                if (cancel)
                    _onError(new OperationCanceledException());
                else if (string.IsNullOrEmpty(existingContents))
                    _parent._store.SaveDocument(_lockName, _parent.ContentsToWrite(), DocumentStoreVersion.At(version), null, OnLockObtained, OnLockBusy, OnError);
                else if (existingContents == _parent._nodeName)
                    OnLockObtained();
                else
                    OnLockBusy();
            }
            private void OnLockMissing()
            {
                _savedVersion = 1;
                _parent._store.SaveDocument(_lockName, _parent.ContentsToWrite(), DocumentStoreVersion.New, null, OnLockObtained, OnLockBusy, OnError);
            }
            private bool IsFree(string contents)
            {
                if (string.IsNullOrEmpty(contents))
                    return true;
                var parts = contents.Split(';');
                if (parts.Length != 2 || string.Equals(parts[1], _parent._nodeName, StringComparison.Ordinal))
                    return true;
                DateTime expiration;
                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd hh:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out expiration))
                    return true;
                var now = _parent._timeService.GetUtcTime();
                return expiration <= now;
            }
            private void OnLockBusy()
            {
                bool reportCannotLock;
                lock (_parent._lock)
                {
                    bool scheduleNextAttempt = !_cancel && !_nowait;
                    reportCannotLock = _nowait;
                    if (scheduleNextAttempt)
                        _wait = _parent._timeService.Delay(TimerInterval, OnLockChanged);
                }
                if (reportCannotLock)
                    _onError(new InvalidOperationException("Lock is already locked"));
            }
            private void OnError(Exception exception)
            {
                lock (_parent._lock)
                {
                    if (!_cancel)
                        _cancel = true;
                }
                _onError(exception);
            }
            private void OnLockObtained()
            {
                lock (_parent._lock)
                {
                    _parent._ownedLocks[_lockName] = new OwnedLock(_parent, _lockName, _savedVersion);
                    if (!_cancel)
                    {
                        _cancel = true;
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
                    if (_wait != null)
                        _wait.Dispose();
                    _wait = null;
                }
                _onError(new OperationCanceledException());
            }

            public void Notify()
            {
                OnLockChanged();
            }
        }

        public void Unlock(string lockName)
        {
            lock (_lock)
            {
                OwnedLock ownedLock;
                if (_ownedLocks.TryGetValue(lockName, out ownedLock))
                    ownedLock.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var locker in _lockers)
                    locker.Dispose();
                _lockers.Clear();
                if (_nextTimer != null)
                    _nextTimer.Dispose();
                foreach (var ownedLock in _ownedLocks.Values)
                    ownedLock.Dispose();
                _ownedLocks.Clear();
                if (_listener != null)
                    _listener.Dispose();
            }
        }
        private void NoAction()
        {
        }
        private void IgnoreError(Exception exception)
        {
        }
    }

    public class NodeLockManagerNull : INodeLockManager
    {
        private NullDispose _disposable = new NullDispose();
        private HashSet<string> _ownedLocks = new HashSet<string>();

        private class NullDispose : IDisposable
        {
            public void Dispose() { }
        }

        public IDisposable Lock(string lockName, Action onLocked, Action<Exception> onError, bool nowait)
        {
            lock (_ownedLocks)
            {
                _ownedLocks.Add(lockName);
                onLocked();
                return _disposable;
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

    public class NodeLock : INodeLock
    {
        private INodeLockManager _manager;
        private string _lockName;
        private IDisposable _wait;
        private Action _onLocked;

        public NodeLock(INodeLockManager manager, string lockName)
        {
            _manager = manager;
            _lockName = lockName;
        }

        public void Lock(Action onLocked, Action<Exception> onError)
        {
            _onLocked = onLocked;
            _wait = _manager.Lock(_lockName, OnLocked, onError, false);
        }

        private void OnLocked()
        {
            _wait = null;
            _onLocked();
        }

        public void Unlock()
        {
            _manager.Unlock(_lockName);
        }

        public void Dispose()
        {
            if (_wait != null)
                _wait.Dispose();
            _wait = null;
        }

        public bool IsLocked
        {
            get { return _manager.IsLocked(_lockName); }
        }
    }

}
