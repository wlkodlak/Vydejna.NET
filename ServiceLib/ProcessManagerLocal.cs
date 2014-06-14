using log4net;
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
        private readonly ITime _time;
        private readonly Dictionary<string, ProcessInfo> _processes;
        private IBus _localBus;
        private ManualResetEventSlim _waitForStop;
        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;
        private IProcessWorker _mainWorker;
        private bool _isShutingDown;
        private readonly ILog _logger;
        
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
            _logger = LogManager.GetLogger("ServiceLib.ProcessManager");
        }

        public void RegisterLocal(string name, IProcessWorker worker)
        {
            _logger.DebugFormat("Adding local service {0}", name);
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            process.IsLocal = true;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _waitForStop.Reset();
            _processes.Add(name, process);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            _logger.DebugFormat("Adding global service {0}", name);
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            process.IsLocal = false;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _waitForStop.Reset();
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

        private bool IsProcessStopped(ProcessState state)
        {
            switch (state)
            {
                case ProcessState.Conflicted:
                case ProcessState.Faulted:
                case ProcessState.Inactive:
                case ProcessState.Uninitialized:
                    return true;
                default:
                    return false;
            }
        }

        private void OnStateChanged(ProcessInfo process, ProcessState state)
        {
            if (IsProcessStopped(state))
            {
                var allStopped = _processes.All(p => IsProcessStopped(p.Value.Worker.State));
                if (allStopped)
                    _waitForStop.Set();
            }

            if (_isShutingDown || !process.RequestedState)
                return;
            if (state == ProcessState.Conflicted)
            {
                _waitForStop.Reset();
                process.Worker.Start();
            }
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
                    _logger.InfoFormat("Starting service {0}", process.Name);
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
                _logger.InfoFormat("Stopping service {0}", process.Name);
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
                    _logger.InfoFormat("Starting service {0}", process.Name);
                        process.Worker.Start();
                        break;
                }
            }
            else
            {
                var state = process.Worker.State;
                if (state == ProcessState.Starting || state == ProcessState.Pausing)
                {
                    _logger.InfoFormat("Stopping service {0}", process.Name);
                    process.Worker.Stop();
                }
                else if (state == ProcessState.Running)
                {
                    _logger.InfoFormat("Stopping service {0}", process.Name);
                    process.Worker.Pause();
                }
            }
        }
    }
}
