using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ProcessManagerCluster
        : IProcessManager
            , IHandle<SystemEvents.SystemInit>
            , IHandle<SystemEvents.SystemShutdown>
            , IHandle<ProcessManagerCluster.ElectionsTimer>
            , IHandle<ProcessManagerCluster.HeartbeatTimer>
            , IHandle<ProcessManagerCluster.ProcessDelayed>
            , IHandle<ProcessManagerCluster.ProcessSchedule>
            , IHandle<ProcessManagerMessages.ConnectionRestored>
            , IHandle<ProcessManagerMessages.ElectionsInquiry>
            , IHandle<ProcessManagerMessages.ElectionsCandidate>
            , IHandle<ProcessManagerMessages.ElectionsLeader>
            , IHandle<ProcessManagerMessages.Heartbeat>
            , IHandle<ProcessManagerMessages.HeartStopped>
            , IHandle<ProcessManagerMessages.ProcessStart>
            , IHandle<ProcessManagerMessages.ProcessStop>
            , IHandle<ProcessManagerMessages.ProcessChange>
            , IHandle<ProcessManagerMessages.ProcessRequest>
    {
        private static readonly ProcessManagerClusterTraceSource Logger
            = new ProcessManagerClusterTraceSource("ServiceLib.ProcessManager");

        private NodeInfo _leader;
        private bool _isLeader, _scheduleNeeded, _isShutingDown;
        private string _nodeId;
        private string _electionsId;
        private DateTime _lastSentTime;
        private ElectionsState _electionsState;
        private readonly Dictionary<string, NodeInfo> _nodes;
        private readonly Dictionary<string, ProcessInfo> _processes;
        private IBus _localBus;
        private IProcessWorker _mainWorker;
        private readonly IPublisher _globalPublisher;
        private readonly ITime _time;
        private readonly object _waitForStop;
        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;

        public class ProcessSchedule
        {
        }

        public class ProcessDelayed
        {
            public object Process { get; set; }
        }

        public class ElectionsTimer
        {
            public string ElectionsId;
        }

        public class HeartbeatTimer
        {
        }

        private class NodeInfo
        {
            public string NodeId;
            public int ProcessCount;
            public bool IsOnline;
            public DateTime LastHeartbeat;
        }

        private class ProcessInfo
        {
            public string Name;
            public IProcessWorker Worker;
            public bool IsLocal, IsAssignedHere;
            public NodeInfo AssignedNode;
            public readonly HashSet<string> PenalizedNodes = new HashSet<string>();
            public readonly HashSet<string> UnsupportedNodes = new HashSet<string>();
            public GlobalProcessState State = GlobalProcessState.Offline;
            public ProcessManagerCluster Parent;
            public bool RequestedState;
            public DateTime ChangeRequested;

            public void OnStateChanged(ProcessState state)
            {
                Parent.OnStateChanged(this, state);
            }
        }

        private enum ElectionsState
        {
            None,
            Losing,
            Winning
        }

        public ProcessManagerCluster(ITime time, string nodeId, IPublisher globalPublisher)
        {
            _nodeId = nodeId;
            _nodes = new Dictionary<string, NodeInfo>();
            _processes = new Dictionary<string, ProcessInfo>();
            _lastSentTime = DateTime.MinValue;
            _time = time;
            _globalPublisher = globalPublisher;
            _waitForStop = new object();
        }

        public ProcessManagerCluster UseNodeId(string nodeId)
        {
            _nodeId = nodeId;
            return this;
        }

        public void RegisterLocal(string name, IProcessWorker worker)
        {
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.IsLocal = true;
            process.RequestedState = true;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
            _processes.Add(name, process);
            Logger.RegisteredLocalService(name);
        }

        public void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost)
        {
            var process = new ProcessInfo();
            process.Parent = this;
            process.Name = name;
            process.Worker = worker;
            process.RequestedState = true;
            worker.Init(process.OnStateChanged, TaskScheduler.Current);
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
            bus.Subscribe<ElectionsTimer>(this);
            bus.Subscribe<HeartbeatTimer>(this);
            bus.Subscribe<ProcessDelayed>(this);
            bus.Subscribe<ProcessSchedule>(this);
            bus.Subscribe<ProcessManagerMessages.ConnectionRestored>(this);
            bus.Subscribe<ProcessManagerMessages.ElectionsInquiry>(this);
            bus.Subscribe<ProcessManagerMessages.ElectionsCandidate>(this);
            bus.Subscribe<ProcessManagerMessages.ElectionsLeader>(this);
            bus.Subscribe<ProcessManagerMessages.Heartbeat>(this);
            bus.Subscribe<ProcessManagerMessages.HeartStopped>(this);
            bus.Subscribe<ProcessManagerMessages.ProcessStart>(this);
            bus.Subscribe<ProcessManagerMessages.ProcessStop>(this);
            bus.Subscribe<ProcessManagerMessages.ProcessChange>(this);
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
            lock (_waitForStop)
            {
                var waitUntil = _time.GetUtcTime().AddSeconds(5);
                while (true)
                {
                    var anythingRunning =
                        _processes.Values.Any(p => p.Worker != null && ProcessIsNotStopped(p.Worker.State));
                    if (!anythingRunning)
                        break;
                    if (!Monitor.Wait(_waitForStop, (int) (waitUntil - _time.GetUtcTime()).TotalMilliseconds))
                        break;
                }
            }
        }

        private bool ProcessIsNotStopped(ProcessState processState)
        {
            switch (processState)
            {
                case ProcessState.Conflicted:
                case ProcessState.Faulted:
                case ProcessState.Inactive:
                case ProcessState.Unsupported:
                case ProcessState.Uninitialized:
                    return false;
                default:
                    return true;
            }
        }

        private void Broadcast<T>(T message)
            where T : ProcessManagerMessages.GenericMessage
        {
            _lastSentTime = _time.GetUtcTime();
            message.SenderId = _nodeId;
            _globalPublisher.Publish(message);
        }

        private async void ScheduleSelfMessage<T>(int timeout, T message)
        {
            await _time.Delay(timeout, _cancelToken);
            _localBus.Publish(message);
        }

        private void StartNewElections(bool forfitCandidature = false)
        {
            _electionsId = Guid.NewGuid().ToString();
            if (!forfitCandidature)
            {
                _electionsState = ElectionsState.Winning;
                ScheduleSelfMessage(5000, new ElectionsTimer {ElectionsId = _electionsId});
            }
            Logger.StartingNewElections(_electionsId, forfitCandidature);
            Broadcast(
                new ProcessManagerMessages.ElectionsInquiry
                {
                    ElectionsId = _electionsId,
                    ForfitCandidature = forfitCandidature
                });
        }

        private void RequestRescheduling()
        {
            _localBus.Publish(new ProcessSchedule());
        }

        private NodeInfo GetNode(string nodeId)
        {
            NodeInfo nodeInfo;
            if (!_nodes.TryGetValue(nodeId, out nodeInfo))
            {
                nodeInfo = new NodeInfo();
                nodeInfo.NodeId = nodeId;
                _nodes[nodeId] = nodeInfo;
            }
            return nodeInfo;
        }

        private NodeInfo UpdateNode(string nodeId)
        {
            var nodeInfo = GetNode(nodeId);
            nodeInfo.LastHeartbeat = _time.GetUtcTime();
            if (!nodeInfo.IsOnline)
            {
                Logger.NodeOnline(nodeId);
                nodeInfo.IsOnline = true;
                _scheduleNeeded = true;
            }
            else
            {
                Logger.NodeSentHeartbeat(nodeId);
            }
            return nodeInfo;
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            foreach (var process in _processes.Values)
            {
                if (process.IsLocal)
                {
                    Logger.StartingService(process.Name);
                    process.Worker.Start();
                }
            }

            StartNewElections();

            ScheduleSelfMessage(2000, new HeartbeatTimer());
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
                if (process.IsLocal)
                {
                    Logger.StoppingService(process.Name);
                    process.Worker.Pause();
                }
                else if (process.Worker.State == ProcessState.Running || process.Worker.State == ProcessState.Starting)
                {
                    Logger.StoppingService(process.Name);
                    process.Worker.Pause();
                }
            }

            Broadcast(new ProcessManagerMessages.HeartStopped());
            if (_isLeader)
            {
                StartNewElections(true);
            }
        }

        public void Handle(ProcessManagerMessages.ConnectionRestored message)
        {
            if (_isShutingDown)
                return;

            StartNewElections();
        }

        public void Handle(ProcessManagerMessages.ElectionsInquiry message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
            if (string.Equals(message.SenderId, _nodeId, StringComparison.Ordinal))
                return;
            var nodeIdComparison = message.ForfitCandidature ? 1 : string.CompareOrdinal(_nodeId, message.SenderId);
            if (_isLeader)
            {
                if (nodeIdComparison >= 0)
                {
                    Logger.NodeIsStillLeader(_electionsId);
                    Broadcast(new ProcessManagerMessages.ElectionsLeader {ElectionsId = message.ElectionsId});
                }
            }
            else
            {
                if (nodeIdComparison > 0)
                {
                    _electionsId = message.ElectionsId;
                    _electionsState = ElectionsState.Winning;
                    Logger.NodeParticipatingInElections(_electionsId);
                    Broadcast(new ProcessManagerMessages.ElectionsCandidate {ElectionsId = _electionsId});

                    ScheduleSelfMessage(5000, new ElectionsTimer {ElectionsId = _electionsId});
                }
            }
        }

        public void Handle(ProcessManagerMessages.ElectionsCandidate message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
            if (string.Equals(message.SenderId, _nodeId, StringComparison.Ordinal))
                return;
            var nodeIdComparison = string.CompareOrdinal(_nodeId, message.SenderId);
            if (nodeIdComparison < 0)
            {
                Logger.NodeLosingElections(message.ElectionsId);
                _electionsState = ElectionsState.Losing;
            }
        }

        public void Handle(ProcessManagerMessages.ElectionsLeader message)
        {
            if (_isShutingDown)
                return;
            var sender = UpdateNode(message.SenderId);
            var nodeIdComparison = string.CompareOrdinal(_nodeId, message.SenderId);
            if (nodeIdComparison > 0)
            {
                Logger.NodeWronglyClaimedLeadership(message.ElectionsId, message.SenderId);
                StartNewElections();
            }
            else if (nodeIdComparison == 0)
            {
                _electionsState = ElectionsState.None;
                _isLeader = true;
                _leader = sender;

                foreach (var process in _processes.Values)
                {
                    if (process.IsLocal)
                        continue;
                    process.State = GlobalProcessState.Offline;
                    process.AssignedNode = null;
                    process.PenalizedNodes.Clear();
                    process.UnsupportedNodes.Clear();
                }
                RequestRescheduling();
            }
            else
            {
                Logger.NodeBecameLeader(message.ElectionsId, message.SenderId);
                _electionsState = ElectionsState.None;
                _isLeader = false;
                _leader = sender;
            }
        }

        public void Handle(ElectionsTimer message)
        {
            if (_isShutingDown || _electionsState != ElectionsState.Winning || _electionsId != message.ElectionsId)
                return;
            Logger.NodeIsWinningElections(message.ElectionsId);
            Broadcast(new ProcessManagerMessages.ElectionsLeader {ElectionsId = _electionsId});
        }

        public void Handle(ProcessManagerMessages.Heartbeat message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
        }

        public void Handle(ProcessManagerMessages.HeartStopped message)
        {
            Logger.NodeWentOfflineGracefully(message.SenderId);
            var sender = GetNode(message.SenderId);
            sender.IsOnline = false;
            if (_isLeader)
                _scheduleNeeded = true;
        }

        public void Handle(HeartbeatTimer message)
        {
            if (_isShutingDown)
                return;
            if (_lastSentTime.AddSeconds(3) <= _time.GetUtcTime())
                Broadcast(new ProcessManagerMessages.Heartbeat());
            ScheduleSelfMessage(2000, new HeartbeatTimer());

            var minOnlineTime = _time.GetUtcTime().AddSeconds(-15);
            if (_isLeader)
            {
                foreach (var node in _nodes.Values)
                {
                    if (node.IsOnline && node.LastHeartbeat < minOnlineTime)
                    {
                        Logger.NodeSeemsOffline(node.NodeId);
                        node.IsOnline = false;
                        _scheduleNeeded = true;
                    }
                }
                foreach (var process in _processes.Values)
                {
                    var stateIncomplete = process.State == GlobalProcessState.Starting ||
                                          process.State == GlobalProcessState.Stopping;
                    if (stateIncomplete && process.ChangeRequested < minOnlineTime)
                    {
                        process.State = GlobalProcessState.Offline;
                        _scheduleNeeded = true;
                    }
                }
                if (_scheduleNeeded)
                {
                    _scheduleNeeded = false;
                    RequestRescheduling();
                }
            }
            else
            {
                foreach (var node in _nodes.Values)
                {
                    if (node.IsOnline && node.LastHeartbeat < minOnlineTime)
                    {
                        Logger.NodeSeemsOffline(node.NodeId);
                        node.IsOnline = false;
                    }
                }
                if (_leader != null && !_leader.IsOnline)
                    _leader = null;
                if (_leader == null && _electionsState == ElectionsState.None)
                {
                    StartNewElections();
                }
            }
        }

        public void Handle(ProcessManagerMessages.ProcessStart message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
            if (_leader == null || !string.Equals(_leader.NodeId, message.SenderId))
                return;
            var nodeIsTarget = string.Equals(_nodeId, message.AssignedNode);
            var targetNode = GetNode(message.AssignedNode);
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process) || process.IsLocal)
            {
                Logger.NodeAssignedUnsupportedTask(message.ProcessName);
                if (nodeIsTarget)
                {
                    Broadcast(
                        new ProcessManagerMessages.ProcessChange
                        {
                            ProcessName = message.ProcessName,
                            NewState = ProcessState.Unsupported
                        });
                }
            }
            else if (nodeIsTarget)
            {
                if (!_isLeader)
                    process.AssignedNode = targetNode;
                var state = process.Worker.State;
                switch (state)
                {
                    case ProcessState.Inactive:
                    case ProcessState.Faulted:
                    case ProcessState.Conflicted:
                        Logger.StartingService(process.Name);
                        process.IsAssignedHere = true;
                        process.Worker.Start();
                        break;
                    case ProcessState.Starting:
                    case ProcessState.Running:
                        Logger.ServiceAlreadyRunning(process.Name);
                        Broadcast(
                            new ProcessManagerMessages.ProcessChange
                            {
                                ProcessName = message.ProcessName,
                                NewState = ProcessState.Running
                            });
                        break;
                }
            }
            else
            {
                if (!_isLeader)
                    process.AssignedNode = targetNode;
                var state = process.Worker.State;
                switch (state)
                {
                    case ProcessState.Running:
                    case ProcessState.Starting:
                    case ProcessState.Pausing:
                        process.IsAssignedHere = false;
                        process.Worker.Stop();
                        break;
                }
            }
        }

        public void Handle(ProcessManagerMessages.ProcessStop message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
            if (_leader == null || !string.Equals(_leader.NodeId, message.SenderId))
                return;
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process) || process.IsLocal)
            {
                Logger.NodeUnassignedUnknownTask(message.ProcessName);
                Broadcast(
                    new ProcessManagerMessages.ProcessChange
                    {
                        ProcessName = message.ProcessName,
                        NewState = ProcessState.Inactive
                    });
            }
            else
            {
                var state = process.Worker.State;
                switch (state)
                {
                    case ProcessState.Inactive:
                    case ProcessState.Faulted:
                    case ProcessState.Conflicted:
                        Logger.ServiceAlreadyInactive(process.Name);
                        Broadcast(
                            new ProcessManagerMessages.ProcessChange
                            {
                                ProcessName = message.ProcessName,
                                NewState = ProcessState.Inactive
                            });
                        break;
                    case ProcessState.Starting:
                        Logger.StoppingService(process.Name);
                        process.IsAssignedHere = false;
                        process.Worker.Stop();
                        break;
                    case ProcessState.Running:
                        Logger.StoppingService(process.Name);
                        process.IsAssignedHere = false;
                        process.Worker.Pause();
                        break;
                }
            }
        }

        public void Handle(ProcessManagerMessages.ProcessChange message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process))
                return;
            Logger.ProcessStateChanged(
                message.ProcessName, false, message.SenderId, message.NewState);
            switch (message.NewState)
            {
                case ProcessState.Unsupported:
                    process.UnsupportedNodes.Add(message.SenderId);
                    break;
                case ProcessState.Faulted:
                case ProcessState.Conflicted:
                    process.PenalizedNodes.Add(message.SenderId);
                    break;
                case ProcessState.Running:
                    process.PenalizedNodes.Remove(message.SenderId);
                    break;
            }
            if (process.AssignedNode == null ||
                !string.Equals(process.AssignedNode.NodeId, message.SenderId, StringComparison.Ordinal))
                return;
            switch (message.NewState)
            {
                case ProcessState.Starting:
                case ProcessState.Running:
                    process.State = GlobalProcessState.Online;
                    break;
                case ProcessState.Faulted:
                case ProcessState.Conflicted:
                case ProcessState.Inactive:
                    process.State = GlobalProcessState.Offline;
                    if (_isLeader)
                        RequestRescheduling();
                    break;
            }
        }

        public void Handle(ProcessDelayed message)
        {
            if (_isShutingDown)
                return;
            var process = (ProcessInfo) message.Process;
            Logger.StartingService(process.Name);
            process.Worker.Start();
        }

        public void Handle(ProcessManagerMessages.ProcessRequest message)
        {
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process))
                return;
            if (process.IsLocal)
            {
                if (message.ShouldBeOnline)
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
            else
            {
                Logger.RequestingServiceState(message.ProcessName, message.ShouldBeOnline);
                _scheduleNeeded = true;
                process.RequestedState = message.ShouldBeOnline;
            }
        }

        private void OnStateChanged(ProcessInfo process, ProcessState state)
        {
            lock (_waitForStop)
                Monitor.PulseAll(_waitForStop);
            if (process.IsLocal)
            {
                if (_isShutingDown)
                    return;
                Logger.ProcessStateChanged(process.Name, false, null, state);
                if (state == ProcessState.Conflicted)
                {
                    Logger.RestartingConflictedProcess(process.Name);
                    process.Worker.Start();
                }
                else if (state == ProcessState.Faulted)
                {
                    Logger.SchedulingFailedProcessRestart(process.Name);
                    ScheduleSelfMessage(10000, new ProcessDelayed {Process = process});
                }
            }
            else
            {
                Logger.ProcessStateChanged(process.Name, false, null, state);
                switch (state)
                {
                    case ProcessState.Starting:
                    case ProcessState.Running:
                    case ProcessState.Faulted:
                    case ProcessState.Inactive:
                    case ProcessState.Conflicted:
                        Broadcast(
                            new ProcessManagerMessages.ProcessChange {ProcessName = process.Name, NewState = state});
                        break;
                }
            }
        }

        public void Handle(ProcessSchedule message)
        {
            if (!_isLeader || _leader == null)
                return;
            Logger.ReschedulingGlobalTasks();

            foreach (var process in _processes.Values)
            {
                if (!process.IsLocal && process.AssignedNode != null && !process.AssignedNode.IsOnline)
                    process.State = GlobalProcessState.Offline;
            }

            foreach (var node in _nodes.Values)
                node.ProcessCount = 0;
            _leader.ProcessCount++;
            foreach (var process in _processes.Values)
            {
                if (process.IsLocal || process.AssignedNode == null)
                    continue;
                if (process.State == GlobalProcessState.Online || process.State == GlobalProcessState.Starting)
                    process.AssignedNode.ProcessCount++;
            }

            foreach (var process in _processes.Values)
            {
                if (process.IsLocal || process.State != GlobalProcessState.Offline || !process.RequestedState)
                    continue;
                var foundNode = FindNodeForProcess(process, true, int.MaxValue);
                if (foundNode == null)
                    continue;
                Logger.ScheduledProcessStart(process.Name, foundNode.NodeId);
                process.AssignedNode = foundNode;
                foundNode.ProcessCount++;
                process.State = GlobalProcessState.ToBeStarted;
            }

            foreach (var process in _processes.Values)
            {
                if (process.IsLocal || process.State != GlobalProcessState.Online || process.AssignedNode == null)
                    continue;
                if (process.RequestedState)
                {
                    var foundNode = FindNodeForProcess(process, false, process.AssignedNode.ProcessCount - 2);
                    if (foundNode == null)
                        continue;
                    Logger.ScheduledProcessMove(
                        process.Name, process.AssignedNode.NodeId,
                        foundNode.NodeId);
                    foundNode.ProcessCount++;
                    process.AssignedNode.ProcessCount--;
                    process.State = GlobalProcessState.ToBeStopped;
                }
                else
                {
                    Logger.SchedulerProcessStop(process.Name, process.AssignedNode.NodeId);
                    process.AssignedNode.ProcessCount--;
                    process.State = GlobalProcessState.ToBeStopped;
                }
            }

            var now = _time.GetUtcTime();
            foreach (var process in _processes.Values)
            {
                if (process.State == GlobalProcessState.ToBeStarted)
                {
                    process.State = GlobalProcessState.Starting;
                    process.ChangeRequested = now;
                    Broadcast(
                        new ProcessManagerMessages.ProcessStart
                        {
                            ProcessName = process.Name,
                            AssignedNode = process.AssignedNode.NodeId
                        });
                }
                else if (process.State == GlobalProcessState.ToBeStopped)
                {
                    process.State = GlobalProcessState.Stopping;
                    process.ChangeRequested = now;
                    Broadcast(
                        new ProcessManagerMessages.ProcessStop
                        {
                            ProcessName = process.Name,
                            AssignedNode = process.AssignedNode.NodeId
                        });
                }
            }
        }

        private NodeInfo FindNodeForProcess(ProcessInfo process, bool allowPenalized, int maxLoad)
        {
            NodeInfo anyNode = null;
            NodeInfo nonPenalized = null;

            foreach (var node in _nodes.Values)
            {
                if (!node.IsOnline || node.ProcessCount > maxLoad || process.UnsupportedNodes.Contains(node.NodeId))
                    continue;
                if (anyNode == null || anyNode.ProcessCount > node.ProcessCount)
                    anyNode = node;
                if (!process.PenalizedNodes.Contains(node.NodeId))
                {
                    if (nonPenalized == null || nonPenalized.ProcessCount > node.ProcessCount)
                        nonPenalized = node;
                }
            }

            if (nonPenalized != null)
                return nonPenalized;
            else if (allowPenalized)
                return anyNode;
            else
                return null;
        }

        public List<ProcessManagerMessages.InfoProcess> GetLocalProcesses()
        {
            return _processes.Values
                .Where(p => p.IsLocal && p.Worker != null)
                .Select(
                    p => new ProcessManagerMessages.InfoProcess
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
                NodeName = _nodeId,
                LeaderName = _leader == null ? "(none)" : _leader.NodeId,
                IsConnected = _nodes.Values.Any(n => n.IsOnline && n.NodeId == _nodeId),
                RunningProcesses = _processes.Values
                    .Where(p => !p.IsLocal && p.IsAssignedHere && p.Worker != null)
                    .Select(
                        p => new ProcessManagerMessages.InfoProcess
                        {
                            ProcessName = p.Name,
                            AssignedNode = _nodeId,
                            ProcessStatus = p.Worker.State.ToString()
                        })
                    .ToList()
            };
        }

        public List<ProcessManagerMessages.InfoProcess> GetLeaderProcesses()
        {
            return _processes.Values
                .Where(p => !p.IsLocal)
                .Select(
                    p => new ProcessManagerMessages.InfoProcess
                    {
                        ProcessName = p.Name,
                        AssignedNode = p.AssignedNode == null ? "(none)" : p.AssignedNode.NodeId,
                        ProcessStatus = p.State.ToString()
                    })
                .ToList();
        }
    }


    public class ProcessManagerClusterTraceSource : TraceSource
    {
        public ProcessManagerClusterTraceSource(string name)
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

        public void ProcessStateChanged(string name, bool isLocal, string nodeId, ProcessState newState)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 21, "Process {Name} is now {State}");
            msg.SetProperty("Name", false, name);
            msg.SetProperty("IsLocal", false, isLocal);
            msg.SetProperty("NodeId", false, nodeId);
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
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 33, "Restarting service {Name} after concurrency conflict");
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

        public void StartingNewElections(string electionsId, bool forfitCandidature)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 81, "Starting new elections");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.SetProperty("ForfitCandidature", false, forfitCandidature);
            msg.Log(this);
        }

        public void NodeIsStillLeader(string electionsId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 82, "Node is still leader");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.Log(this);
        }

        public void NodeParticipatingInElections(string electionsId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 83, "Participating in elections");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.Log(this);
        }

        public void NodeLosingElections(string electionsId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 84, "Node is losing elections");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.Log(this);
        }

        public void NodeWronglyClaimedLeadership(string electionsId, string senderId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 85, "Other node wrongly claimed leadership");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.SetProperty("SenderId", false, senderId);
            msg.Log(this);
        }

        public void NodeBecameLeader(string electionsId, string senderId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 86, "Node {SenderId} became leader");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.SetProperty("SenderId", false, senderId);
            msg.Log(this);
        }

        public void NodeIsWinningElections(string electionsId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 86, "Node is winning elections");
            msg.SetProperty("ElectionsId", false, electionsId);
            msg.Log(this);
        }

        public void NodeOnline(string nodeId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 101, "Node {NodeId} went online");
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }

        public void NodeSentHeartbeat(string nodeId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 102, "Node {NodeId} sent a message");
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }

        public void NodeWentOfflineGracefully(string nodeId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 103, "Node {NodeId} went offline gracefully");
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }

        public void NodeSeemsOffline(string nodeId)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 104, "Node {NodeId} seems offline");
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }

        public void NodeAssignedUnsupportedTask(string processName)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 104, "Node was assigned an unsupported task {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void NodeUnassignedUnknownTask(string processName)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 104, "Node was unassigned an unsupported task {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void ServiceAlreadyRunning(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 121, "Service {ProcessName} is already running");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void ServiceAlreadyInactive(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 122, "Service {ProcessName} is already inactive");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void RequestingServiceState(string processName, bool shouldBeOnline)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 125, "Service {ProcessName} should be {RequestedState}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("RequestedState", false, shouldBeOnline ? "online" : "offline");
            msg.Log(this);
        }

        public void ReschedulingGlobalTasks()
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 121, "Starting rescheduling of global tasks");
            msg.Log(this);
        }

        public void ScheduledProcessStart(string processName, string nodeId)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 122, "Service {ProcessName} scheduled to start at {NodeId}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }

        public void ScheduledProcessMove(string processName, string oldNodeId, string newNodeId)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 123,
                "Service {ProcessName} scheduled to move from {OldNodeId} to {NewNodeId}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("OldNodeId", false, oldNodeId);
            msg.SetProperty("NewNodeId", false, newNodeId);
            msg.Log(this);
        }

        public void SchedulerProcessStop(string processName, string nodeId)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 124, "Service {ProcessName} scheduled to start at {NodeId}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("NodeId", false, nodeId);
            msg.Log(this);
        }
    }
}