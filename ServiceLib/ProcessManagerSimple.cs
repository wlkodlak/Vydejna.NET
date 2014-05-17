using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ProcessManagerSimple : IProcessManager, IHandle<SystemEvents.SystemInit>, IHandle<SystemEvents.SystemShutdown>
    {
        private object _lock;
        private Dictionary<string, ProcessInfo> _processes;
        private IBus _bus;
        private IProcessWorker _primaryWorker;

        public ProcessManagerSimple()
        {
            _lock = new object();
            _processes = new Dictionary<string, ProcessInfo>();
        }

        public void RegisterBus(string name, IBus bus, IProcessWorker worker)
        {
            _bus = bus;
            _primaryWorker = worker;
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
        }

        public void Start()
        {
            _bus.Publish(new SystemEvents.SystemInit());
            if (_primaryWorker != null)
                _primaryWorker.Start();
        }

        public void Stop()
        {
            _bus.Publish(new SystemEvents.SystemShutdown());
            if (_primaryWorker != null)
                _primaryWorker.Pause();
        }

        public void RegisterLocal(string name, IProcessWorker worker)
        {
            var process = new ProcessInfo
            {
                Name = name,
                Worker = worker,
                StopEvent = new ManualResetEventSlim()
            };
            Register(process);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            var process = new ProcessInfo
            {
                Name = name,
                Worker = worker,
                StopEvent = new ManualResetEventSlim()
            };
            Register(process);
        }

        public void Handle(SystemEvents.SystemInit msg)
        {
            foreach (var process in _processes)
            {
                process.Value.StopEvent.Reset();
                process.Value.Worker.Start();
            }
        }

        public void Handle(SystemEvents.SystemShutdown msg)
        {
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
            process.Worker.Init(process.OnStateChanged, TaskScheduler.Default);
        }

        private class ProcessInfo
        {
            public string Name;
            public IProcessWorker Worker;
            public ManualResetEventSlim StopEvent;

            public void OnStateChanged(ProcessState newState)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Process {0}: {1}", Name, newState.ToString()));
                if (newState == ProcessState.Inactive || newState == ProcessState.Faulted)
                    StopEvent.Set();
            }
        }

        public bool WaitForStop(int timeout = 2000)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(timeout);
            bool allStopped = true;
            foreach (var process in _processes.Values)
            {
                var state = process.Worker.State;
                if (state == ProcessState.Uninitialized || state == ProcessState.Inactive || state == ProcessState.Faulted)
                    continue;
                var waitMs = Math.Max(1, (int)(endTime - DateTime.UtcNow).TotalMilliseconds);
                allStopped = allStopped | process.StopEvent.Wait(waitMs);
            }
            return allStopped;
        }
    }
}
