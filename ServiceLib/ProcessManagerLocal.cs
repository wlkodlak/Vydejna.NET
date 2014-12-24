using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private static readonly ProcessManagerLocalTraceSource Logger
             = new ProcessManagerLocalTraceSource("ServiceLib.ProcessManager");
        private readonly ITime _time;
        private readonly Dictionary<string, ProcessInfo> _processes;
        private IBus _localBus;
        private readonly ManualResetEventSlim _waitForStop;
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
            _waitForStop.Reset();
            _processes.Add(name, process);
            Logger.RegisteredLocalService(name);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            Logger.RegisteredLocalService(name);
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            process.IsLocal = false;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _waitForStop.Reset();
            _processes.Add(name, process);
            Logger.RegisteredGlobalService(name);
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
            Logger.RegisteredBus(name);
        }

        public void Start()
        {
            _isShutingDown = false;
            _cancelSource = new CancellationTokenSource();
            _cancelToken = _cancelSource.Token;
            _mainWorker.Start();
            _localBus.Publish(new SystemEvents.SystemInit());
            Logger.SystemStarted();
        }

        public void Stop()
        {
            Logger.SystemStopping();
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
            Logger.ProcessStateChanged(process.Name, process.IsLocal, state);
            if (IsProcessStopped(state))
            {
                var allStopped = _processes.All(p => IsProcessStopped(p.Value.Worker.State));
                if (allStopped)
                {
                    Logger.AllProcessesStopped();
                    _waitForStop.Set();
                }
            }

            if (_isShutingDown || !process.RequestedState)
                return;
            if (state == ProcessState.Conflicted)
            {
                try
                {
                    Logger.RestartingConflictedProcess(process.Name);
                    _waitForStop.Reset();
                    process.Worker.Start();
                }
                catch (Exception exception)
                {
                    Logger.ProcessRestartFailed(process.Name, exception);
                }
            }
            else if (state == ProcessState.Faulted)
            {
                Logger.SchedulingFailedProcessRestart(process.Name);
                ScheduleProcessRestart(process);
            }
        }

        private async void ScheduleProcessRestart(ProcessInfo process)
        {
            try
            {
                await _time.Delay(10000, _cancelToken);
                if (_isShutingDown || !process.RequestedState || _cancelToken.IsCancellationRequested)
                    return;
                process.Worker.Start();
            }
            catch (Exception exception)
            {
                Logger.ProcessRestartFailed(process.Name, exception);
            }
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            foreach (var process in _processes.Values)
            {
                if (process.RequestedState)
                {
                    Logger.StartingService(process.Name);
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
                Logger.StoppingService(process.Name);
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
                        Logger.StartingService(process.Name);
                        process.Worker.Start();
                        break;
                }
            }
            else
            {
                var state = process.Worker.State;
                if (state == ProcessState.Starting || state == ProcessState.Pausing)
                {
                    Logger.StoppingService(process.Name);
                    process.Worker.Stop();
                }
                else if (state == ProcessState.Running)
                {
                    Logger.StoppingService(process.Name);
                    process.Worker.Pause();
                }
            }
        }
    }

    public class ProcessManagerLocalTraceSource : TraceSource
    {
        public ProcessManagerLocalTraceSource(string name)
            : base(name)
        {
        }

        public void RegisteredLocalService(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Registered local service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void RegisteredGlobalService(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "Registered global service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void RegisteredBus(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 3, "Registered bus service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void SystemStarted()
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 7, "System started");
            msg.Log(this);
        }

        public void SystemStopping()
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 8, "System started");
            msg.Log(this);
        }

        public void ProcessStateChanged(string name, bool isLocal, ProcessState newState)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 21, "Process {Name} is now {State}");
            msg.SetProperty("Name", false, name);
            msg.SetProperty("IsLocal", false, isLocal);
            msg.SetProperty("State", false, newState);
            msg.Log(this);
        }

        public void AllProcessesStopped()
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 22, "All processes stopped");
            msg.Log(this);
        }

        public void StartingService(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 31, "Starting service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void StoppingService(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 32, "Stopping service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void RestartingConflictedProcess(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 33, "Restarting service {Name} after concurrency conflict");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void SchedulingFailedProcessRestart(string name)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 34, "Scheduling restart for service {Name}");
            msg.SetProperty("Name", false, name);
            msg.Log(this);
        }

        public void ProcessRestartFailed(string name, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 39, "Start of service {Name} failed");
            msg.SetProperty("Name", false, name);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }
}
