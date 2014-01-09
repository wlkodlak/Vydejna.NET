using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestExecutor : IQueueExecution
    {
        private object _lock = new object();
        private Queue<IQueuedExecutionDispatcher> _queue = new Queue<IQueuedExecutionDispatcher>();
        private int _attachedProcesses = 0;
        private EmptyHandler _empty = new EmptyHandler();

        public void Enqueue(IQueuedExecutionDispatcher handler)
        {
            lock (_lock)
            {
                _queue.Enqueue(handler);
                Monitor.PulseAll(_lock);
            }
        }

        public void Process()
        {
            for (int i = 0; i < 2000; i++)
            {
                var handler = GetNextHandler();
                if (handler == null)
                    return;
                handler.Execute();
            }
        }

        private IQueuedExecutionDispatcher GetNextHandler()
        {
            lock (_lock)
            {
                if (_queue.Count > 0)
                    return _queue.Dequeue();
                if (_attachedProcesses == 0)
                    return null;
                Monitor.Wait(_lock, 50);
                return _empty;
            }
        }

        public IDisposable AttachBusyProcess()
        {
            lock (_lock)
                _attachedProcesses++;
            return new AttachedProcess(this);
        }

        private class EmptyHandler : IQueuedExecutionDispatcher
        {
            public void Execute() { }
        }

        private class AttachedProcess : IDisposable
        {
            private TestExecutor _parent;
            public AttachedProcess(TestExecutor parent)
            {
                _parent = parent;
            }
            public void Dispose()
            {
                lock (_parent._lock)
                    _parent._attachedProcesses--;
            }
        }
    }
}
