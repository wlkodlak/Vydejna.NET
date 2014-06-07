using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ProcessManagerLocal
        : IProcessManager
        , IHandle<SystemEvents.SystemInit>
        , IHandle<SystemEvents.SystemShutdown>
        , IHandle<ProcessManagerMessages.ProcessRequest>
    {
        private ITime _time;
        private Dictionary<string, ProcessInfo> _processes;
        private IBus _localBus;
        private ManualResetEventSlim _waitForStop;
        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;
        private IProcessWorker _mainWorker;
        private bool _isShutingDown;
        
        private class ProcessInfo
        {
            public ProcessManagerLocal Parent;
            public string Name;
            public IProcessWorker Worker;
            public bool RequestedState, IsLocal;

            public void OnStateChanged(ProcessState state)
            {
                Parent.OnStateChanged(this, state);
            }
        }

        public ProcessManagerLocal(ITime time)
        {
            _time = time;
            _processes = new Dictionary<string, ProcessInfo>();
            _waitForStop = new ManualResetEventSlim();
            _waitForStop.Set();
        }

        public void RegisterLocal(string name, IProcessWorker worker)
        {
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            process.IsLocal = true;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _processes.Add(name, process);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            process.IsLocal = false;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _processes.Add(name, process);
        }

        public void RegisterBus(string name, IBus bus, IProcessWorker worker)
        {
            _localBus = bus;
            _mainWorker = worker;
            worker.Init(null, TaskScheduler.Default);
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
            bus.Subscribe<ProcessManagerMessages.ProcessRequest>(this);
            bus.Subscribe<ProcessManagerMessages.ProcessRequest>(this);
        }

        public void Start()
        {
            _isShutingDown = false;
            _cancelSource = new CancellationTokenSource();
            _cancelToken = _cancelSource.Token;
            _mainWorker.Start();
            _localBus.Publish(new SystemEvents.SystemInit());
        }

        public void Stop()
        {
            _localBus.Publish(new SystemEvents.SystemShutdown());
            _mainWorker.Pause();
        }

        public void WaitForStop()
        {
            _waitForStop.Wait(5000);
        }

        public List<ProcessManagerMessages.InfoProcess> GetLocalProcesses()
        {
            return _processes.Values
                .Where(p => p.IsLocal && p.Worker != null)
                .Select(p => new ProcessManagerMessages.InfoProcess
                {
                    ProcessName = p.Name,
                    ProcessStatus = p.Worker.State.ToString()
                })
                .ToList();
        }

        public ProcessManagerMessages.InfoGlobal GetGlobalInfo()
        {
            return new ProcessManagerMessages.InfoGlobal
            {
                NodeName = "(local)",
                LeaderName = "(local)",
                IsConnected = true,
                RunningProcesses = _processes.Values
                    .Where(p => !p.IsLocal && p.Worker != null)
                    .Select(p => new ProcessManagerMessages.InfoProcess
                    {
                        ProcessName = p.Name,
                        AssignedNode = "(local)",
                        ProcessStatus = p.Worker.State.ToString()
                    })
                    .ToList()
            };
        }

        public List<ProcessManagerMessages.InfoProcess> GetLeaderProcesses()
        {
            return _processes.Values
                .Where(p => !p.IsLocal)
                .Select(p => new ProcessManagerMessages.InfoProcess
                {
                    ProcessName = p.Name,
                    AssignedNode = p.RequestedState ? "(local)" : "(none)",
                    ProcessStatus = p.Worker.State.ToString()
                })
                .ToList();
        }

        private void OnStateChanged(ProcessInfo process, ProcessState state)
        {
            if (_isShutingDown || !process.RequestedState)
                return;
            if (state == ProcessState.Conflicted)
                process.Worker.Start();
            else if (state == ProcessState.Faulted)
            {
                _time.Delay(10000, _cancelToken).ContinueWith(task =>
                {
                    if (_isShutingDown || !process.RequestedState)
                        return;
                    process.Worker.Start();
                });
            }
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            foreach (var process in _processes.Values)
            {
                if (process.RequestedState)
                {
                    process.Worker.Start();
                }
            }
        }

        public void Handle(SystemEvents.SystemShutdown message)
        {
            _isShutingDown = true;
            if (_cancelSource != null)
            {
                _cancelSource.Cancel();
                _cancelSource.Dispose();
                _cancelSource = null;
            }

            foreach (var process in _processes.Values)
            {
                process.RequestedState = false;
                process.Worker.Pause();
            }
        }

        public void Handle(ProcessManagerMessages.ProcessRequest message)
        {
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process))
                return;
            process.RequestedState = message.ShouldBeOnline;

            if (process.RequestedState)
            {
                switch (process.Worker.State)
                {
                    case ProcessState.Inactive:
                    case ProcessState.Faulted:
                    case ProcessState.Conflicted:
                        process.Worker.Start();
                        break;
                }
            }
            else
            {
                var state = process.Worker.State;
                if (state == ProcessState.Starting || state == ProcessState.Pausing)
                    process.Worker.Stop();
                else if (state == ProcessState.Running)
                    process.Worker.Pause();
            }
        }
    }
}
