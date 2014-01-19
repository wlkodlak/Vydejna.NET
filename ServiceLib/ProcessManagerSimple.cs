using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ProcessManagerSimple : IProcessManager, IHandle<SystemEvents.SystemInit>, IHandle<SystemEvents.SystemShutdown>
    {
        private object _lock;
        private Dictionary<string, ProcessInfo> _processes;
        private ProcessState _state;

        public ProcessManagerSimple()
        {
            _lock = new object();
            _processes = new Dictionary<string, ProcessInfo>();
            _state = ProcessState.Inactive;
        }

        public void RegisterLocal(string name, IProcessWorker worker)
        {
            var process = new ProcessInfo
            {
                Name = name, Worker = worker
            };
            Register(process);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            var process = new ProcessInfo
            {
                Name = name,
                Worker = worker
            };
            Register(process);
        }

        public void Subscribe(IBus bus)
        {
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
        }

        public void Handle(SystemEvents.SystemInit msg)
        {
            _state = ProcessState.Running;
            foreach (var process in _processes)
                process.Value.Worker.Start();
        }

        public void Handle(SystemEvents.SystemShutdown msg)
        {
            _state = ProcessState.Stopping;
            foreach (var process in _processes)
            {
                var worker = process.Value.Worker;
                if (worker.State == ProcessState.Running)
                    worker.Pause();
                else if (worker.State == ProcessState.Starting)
                    worker.Stop();
            }
        }

        private void Register(ProcessInfo process)
        {
            _processes[process.Name] = process;
            process.Worker.Init(process.OnStateChanged);
        }

        private class ProcessInfo
        {
            public string Name;
            public IProcessWorker Worker;

            public void OnStateChanged(ProcessState newState)
            {
            }
        }

    }
}
