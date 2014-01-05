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
        private Action _cannotLock;
        public bool IsWaiting { get; private set; }
        public bool IsLocked { get; private set; }
        
        public void Lock(Action onLocked, Action cannotLock)
        {
            _onLocked = onLocked;
            _cannotLock = cannotLock;
            IsWaiting = true;
        }

        public void SendLock(bool obtained = true)
        {
            IsWaiting = false;
            IsLocked = obtained;
            if (obtained)
                _onLocked();
            else
                _cannotLock();
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
                _cannotLock();
            }
        }
    }
}
