using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class UpdateLock
    {
        private object _lock = new object();
        private int _readers = 0;
        private bool _updating = false;
        private bool _writing = false;

        public IDisposable Lock()
        {
            lock (_lock)
            {
                while (_writing)
                    Monitor.Wait(_lock);
                while (_readers > 0)
                    Monitor.Wait(_lock);
                _writing = _updating = true;
                return new DisposeLock(this, 1);
            }
        }

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
}
