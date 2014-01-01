using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestExecutor : IQueueExecution
    {
        private Queue<IQueuedExecutionDispatcher> _queue = new Queue<IQueuedExecutionDispatcher>();

        public void Enqueue(IQueuedExecutionDispatcher handler)
        {
            _queue.Enqueue(handler);
        }

        public void Process()
        {
            for (int i = 0; _queue.Count > 0; i++)
            {
                if (i < 2000)
                    _queue.Dequeue().Execute();
                else
                    throw new InvalidOperationException("Endless loop");
            }
        }
    }
}
