using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class UpdateLock
    {
        private object _lock = new object();
        private int _readers = 0;
        private bool _updating = false;
        private bool _writing = false;

        public IDisposable Read()
        {
            lock (_lock)
            {
                while (_writing)
                    Monitor.Wait(_lock);
                _readers++;
                return new DisposeLock(this, 0);
            }
        }

        public IDisposable Update()
        {
            lock (_lock)
            {
                while (_updating)
                    Monitor.Wait(_lock);
                _updating = true;
                return new DisposeLock(this, 1);
            }
        }

        public void Write()
        {
            lock (_lock)
            {
                while (_readers > 0)
                    Monitor.Wait(_lock);
                _writing = _updating = true;
            }
        }

        private void Unlock(int mode)
        {
            lock (_lock)
            {
                if (mode == 0)
                    _readers--;
                else if (mode == 1)
                    _updating = _writing = false;
                Monitor.PulseAll(_lock);
            }
        }

        private class DisposeLock : IDisposable
        {
            private UpdateLock _lock;
            private int _mode;
            public DisposeLock(UpdateLock updateLock, int mode)
            {
                _lock = updateLock;
                _mode = mode;
            }
            public void Dispose()
            {
                GC.SuppressFinalize(this);
                Dispose(true);
            }
            private void Dispose(bool disposing)
            {
                if (_lock != null)
                    _lock.Unlock(_mode);
            }
            ~DisposeLock()
            {
                Dispose(false);
            }
        }
    }

    public class AsyncLock
    {
        private object _lock;
        private bool _locked;
        private Queue<TaskCompletionSource<IDisposable>> _queue;

        public AsyncLock()
        {
            _locked = false;
            _lock = new object();
            _queue = new Queue<TaskCompletionSource<IDisposable>>();
        }

        public Task<IDisposable> Lock()
        {
            lock (_lock)
            {
                if (_locked)
                {
                    var tcs = new TaskCompletionSource<IDisposable>();
                    _queue.Enqueue(tcs);
                    return tcs.Task;
                }
                else
                {
                    _locked = true;
                    return Task.FromResult<IDisposable>(new DisposeLock(this));
                }
            }
        }

        private void Unlock()
        {
            TaskCompletionSource<IDisposable> tcs = null;
            lock (_lock)
            {
                if (_queue.Count == 0)
                    _locked = false;
                else
                    tcs = _queue.Dequeue();
            }
            if (tcs != null)
                tcs.SetResult(new DisposeLock(this));
        }

        private class DisposeLock : IDisposable
        {
            private AsyncLock _lock;
            public DisposeLock(AsyncLock asyncLock)
            {
                _lock = asyncLock;
            }
            public void Dispose()
            {
                GC.SuppressFinalize(this);
                Dispose(true);
            }
            private void Dispose(bool disposing)
            {
                if (_lock != null)
                {
                    _lock.Unlock();
                    _lock = null;
                }
            }
            ~DisposeLock()
            {
                Dispose(false);
            }
        }
    }
}
