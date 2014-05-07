using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestNodeLock : INodeLock
    {
        private Action _onLocked;
        private Action<Exception> _onError;
        public bool IsWaiting { get; private set; }
        public bool IsLocked { get; private set; }
        
        public void Lock(Action onLocked, Action<Exception> onError)
        {
            _onLocked = onLocked;
            _onError = onError;
            IsWaiting = true;
        }

        public void SendLock()
        {
            IsWaiting = false;
            IsLocked = true;
            _onLocked();
        }

        public void Unlock()
        {
            IsLocked = false;
        }

        public void Dispose()
        {
            if (IsWaiting)
            {
                IsWaiting = false;
                _onError(new OperationCanceledException());
            }
        }
    }
}
