using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ServiceLib
{
    public class ProcessManagerPostgres
        : IProcessManager
    {
        public void RegisterLocal(string name, IProcessWorker worker)
        {
            throw new NotImplementedException();
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            throw new NotImplementedException();
        }

        public void RegisterBus(string name, IBus bus, IProcessWorker worker)
        {
            throw new NotImplementedException();
        }

        public void Start()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }

        public void WaitForStop()
        {
            throw new NotImplementedException();
        }
    }
}
