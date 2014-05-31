using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public static class ProcessManagerMessages
    {
        public class GenericMessage
        {
            public string SenderId { get; set; }
        }

        public class ElectionsInquiry : GenericMessage
        {
            public string ElectionsId { get; set; }
            public bool ForfitCandidature { get; set; }
        }
        public class ElectionsCandidate : GenericMessage
        {
            public string ElectionsId { get; set; }
        }
        public class ElectionsLeader : GenericMessage
        {
            public string ElectionsId { get; set; }
        }

        public class Heartbeat : GenericMessage { }
        public class HeartStopped : GenericMessage { }

        public class ProcessMessage : GenericMessage
        {
            public string AssignedNode { get; set; }
            public string ProcessName { get; set; }
        }
        public class ProcessStart : ProcessMessage { }
        public class ProcessStop : ProcessMessage { }
        public class ProcessChange : GenericMessage
        {
            public string ProcessName { get; set; }
            public ProcessState NewState { get; set; }
        }

        public class ConnectionRestored { }
        public class ProcessRequest
        {
            public string ProcessName { get; set; }
            public bool ShouldBeOnline { get; set; }
        }
    }

    public enum GlobalProcessState
    {
        Offline, Starting, Online, Stopping, ToBeStarted, ToBeStopped, Unsupported
    }

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
        private NodeInfo _leader;
        private bool _isLeader, _scheduleNeeded, _isShutingDown;
        private string _nodeId;
        private string _electionsId;
        private DateTime _lastSentTime;
        private ElectionsState _electionsState;
        private Dictionary<string, NodeInfo> _nodes;
        private Dictionary<string, ProcessInfo> _processes;
        private IBus _localBus;
        private IProcessWorker _mainWorker;
        private IPublisher _globalPublisher;
        private ITime _time;
        private ManualResetEventSlim _waitForStop;
        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;

        public class ProcessSchedule { }
        public class ProcessDelayed
        {
            public object Process { get; set; }
        }
        public class ElectionsTimer
        {
            public string ElectionsId;
        }
        public class HeartbeatTimer { }

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
            public HashSet<string> PenalizedNodes = new HashSet<string>();
            public HashSet<string> UnsupportedNodes = new HashSet<string>();
            public GlobalProcessState State = GlobalProcessState.Offline;
            public ProcessManagerCluster Parent;
            public bool RequestedState;

            public void OnStateChanged(ProcessState state)
            {
                Parent.OnStateChanged(this, state);
            }
        }

        private enum ElectionsState
        {
            None, Losing, Winning
        }

        public ProcessManagerCluster(ITime time, IPublisher globalPublisher)
        {
            _nodeId = Guid.NewGuid().ToString();
            _nodes = new Dictionary<string, NodeInfo>();
            _processes = new Dictionary<string, ProcessInfo>();
            _processes = new Dictionary<string, ProcessInfo>();
            _lastSentTime = DateTime.MinValue;
            _time = time;
            _globalPublisher = globalPublisher;
            _waitForStop = new ManualResetEventSlim();
            _waitForStop.Set();
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
        }

        public void RegisterBus(string name, IBus bus, IProcessWorker worker)
        {
            _localBus = bus;
            _mainWorker = worker;
            worker.Init(null, TaskScheduler.Default);
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
            bus.Subscribe<ProcessManagerCluster.ElectionsTimer>(this);
            bus.Subscribe<ProcessManagerCluster.HeartbeatTimer>(this);
            bus.Subscribe<ProcessManagerCluster.ProcessDelayed>(this);
            bus.Subscribe<ProcessManagerCluster.ProcessSchedule>(this);
        }

        public void SubscribeGlobals(IBus bus)
        {
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

        private void Broadcast<T>(T message)
            where T : ProcessManagerMessages.GenericMessage
        {
            _lastSentTime = _time.GetUtcTime();
            message.SenderId = _nodeId;
            _globalPublisher.Publish(message);
        }

        private void ScheduleSelfMessage<T>(int timeout, T message)
        {
            _time.Delay(timeout, _cancelToken).ContinueWith(task => _localBus.Publish(message));
        }

        private void StartNewElections(bool forfitCandidature = false)
        {
            _electionsId = Guid.NewGuid().ToString();
            if (!forfitCandidature)
            {
                _electionsState = ElectionsState.Winning;
                ScheduleSelfMessage(5000, new ProcessManagerCluster.ElectionsTimer { ElectionsId = _electionsId });
            }
            Broadcast(new ProcessManagerMessages.ElectionsInquiry { ElectionsId = _electionsId, ForfitCandidature = forfitCandidature });
        }

        private void RequestRescheduling()
        {
            _localBus.Publish(new ProcessManagerCluster.ProcessSchedule());
        }

        private NodeInfo GetNode(string nodeId)
        {
            NodeInfo nodeInfo;
            if (_nodes.TryGetValue(nodeId, out nodeInfo))
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
                nodeInfo.IsOnline = true;
                _scheduleNeeded = true;
            }
            return nodeInfo;
        }

        public void Handle(SystemEvents.SystemInit message)
        {
            foreach (var process in _processes.Values)
            {
                if (process.IsLocal)
                {
                    process.Worker.Start();
                }
            }

            StartNewElections();

            ScheduleSelfMessage(2000, new ProcessManagerCluster.HeartbeatTimer());
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
                    process.Worker.Pause();
                else if (process.Worker.State == ProcessState.Running || process.Worker.State == ProcessState.Starting)
                    process.Worker.Pause();
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
            if (string.Equals(message.SenderId, _nodeId, StringComparison.Ordinal))
                return;
            UpdateNode(message.SenderId);
            var nodeIdComparison = message.ForfitCandidature ? 1 : string.CompareOrdinal(_nodeId, message.SenderId);
            if (_isLeader)
            {
                if (nodeIdComparison >= 0)
                {
                    Broadcast(new ProcessManagerMessages.ElectionsLeader { ElectionsId = message.ElectionsId });
                }
            }
            else
            {
                if (nodeIdComparison > 0)
                {
                    _electionsId = message.ElectionsId;
                    _electionsState = ElectionsState.Winning;
                    Broadcast(new ProcessManagerMessages.ElectionsCandidate { ElectionsId = _electionsId });

                    ScheduleSelfMessage(5000, new ProcessManagerCluster.ElectionsTimer { ElectionsId = _electionsId });
                }
            }
        }

        public void Handle(ProcessManagerMessages.ElectionsCandidate message)
        {
            if (_isShutingDown)
                return;
            if (string.Equals(message.SenderId, _nodeId, StringComparison.Ordinal))
                return;
            UpdateNode(message.SenderId);
            var nodeIdComparison = string.CompareOrdinal(_nodeId, message.SenderId);
            if (nodeIdComparison < 0)
            {
                _electionsState = ElectionsState.Losing;
            }
        }

        public void Handle(ProcessManagerMessages.ElectionsLeader message)
        {
            if (_isShutingDown)
                return;
            if (string.Equals(message.SenderId, _nodeId, StringComparison.Ordinal))
                return;
            var sender = UpdateNode(message.SenderId);
            var nodeIdComparison = string.CompareOrdinal(_nodeId, message.SenderId);
            if (nodeIdComparison > 0)
            {
                StartNewElections();
            }
            else
            {
                _electionsState = ElectionsState.None;
                _isLeader = false;
                _leader = sender;
            }
        }

        public void Handle(ProcessManagerCluster.ElectionsTimer message)
        {
            if (_isShutingDown || _electionsState != ElectionsState.Winning || _electionsId != message.ElectionsId)
                return;
            _electionsState = ElectionsState.None;
            _isLeader = true;
            _leader = GetNode(_nodeId);
            foreach (var process in _processes.Values)
            {
                if (process.IsLocal)
                    continue;
                process.State = GlobalProcessState.Offline;
                process.AssignedNode = null;
                process.PenalizedNodes.Clear();
                process.UnsupportedNodes.Clear();
            }
            Broadcast(new ProcessManagerMessages.ElectionsLeader { ElectionsId = _electionsId });
            RequestRescheduling();
        }

        public void Handle(ProcessManagerMessages.Heartbeat message)
        {
            if (_isShutingDown)
                return;
            UpdateNode(message.SenderId);
        }

        public void Handle(ProcessManagerMessages.HeartStopped message)
        {
            var sender = GetNode(message.SenderId);
            sender.IsOnline = false;
            if (_isLeader)
                _scheduleNeeded = true;
        }

        public void Handle(ProcessManagerCluster.HeartbeatTimer message)
        {
            if (_isShutingDown)
                return;
            if (_lastSentTime.AddSeconds(3) <= _time.GetUtcTime())
                Broadcast(new ProcessManagerMessages.Heartbeat());
            ScheduleSelfMessage(2000, new ProcessManagerCluster.HeartbeatTimer());

            var minOnlineTime = _time.GetUtcTime().AddSeconds(-15);
            if (_isLeader)
            {
                foreach (var node in _nodes.Values)
                {
                    if (node.IsOnline && node.LastHeartbeat < minOnlineTime)
                    {
                        node.IsOnline = false;
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
            if (_isShutingDown || _leader == null || !string.Equals(_leader.NodeId, message.SenderId) || !string.Equals(_nodeId, message.AssignedNode))
                return;
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process) || process.IsLocal)
            {
                Broadcast(new ProcessManagerMessages.ProcessChange { ProcessName = message.ProcessName, NewState = ProcessState.Unsupported });
            }
            else
            {
                var state = process.Worker.State;
                switch (state)
                {
                    case ProcessState.Inactive:
                    case ProcessState.Faulted:
                    case ProcessState.Conflicted:
                        process.IsAssignedHere = true;
                        process.Worker.Start();
                        break;
                    case ProcessState.Starting:
                    case ProcessState.Running:
                        Broadcast(new ProcessManagerMessages.ProcessChange { ProcessName = message.ProcessName, NewState = ProcessState.Running });
                        break;
                }
            }
        }

        public void Handle(ProcessManagerMessages.ProcessStop message)
        {
            if (_isShutingDown || _leader == null || !string.Equals(_leader.NodeId, message.SenderId))
                return;
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process) || process.IsLocal)
            {
                Broadcast(new ProcessManagerMessages.ProcessChange { ProcessName = message.ProcessName, NewState = ProcessState.Inactive });
            }
            else
            {
                var state = process.Worker.State;
                switch (state)
                {
                    case ProcessState.Inactive:
                    case ProcessState.Faulted:
                    case ProcessState.Conflicted:
                        Broadcast(new ProcessManagerMessages.ProcessChange { ProcessName = message.ProcessName, NewState = ProcessState.Inactive });
                        break;
                    case ProcessState.Starting:
                        process.IsAssignedHere = false;
                        process.Worker.Stop();
                        break;
                    case ProcessState.Running:
                        process.IsAssignedHere = false;
                        process.Worker.Pause();
                        break;
                }
            }
        }

        public void Handle(ProcessManagerMessages.ProcessChange message)
        {
            if (!_isLeader || _isShutingDown)
                return;
            ProcessInfo process;
            if (!_processes.TryGetValue(message.ProcessName, out process))
                return;
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
            if (process.AssignedNode == null || !string.Equals(process.AssignedNode.NodeId, message.SenderId, StringComparison.Ordinal))
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
                    RequestRescheduling();
                    break;
            }
        }

        public void Handle(ProcessManagerCluster.ProcessDelayed message)
        {
            if (_isShutingDown)
                return;
            var process = message.Process as ProcessInfo;
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
            else
            {
                _scheduleNeeded = true;
                process.RequestedState = message.ShouldBeOnline;
            }
        }

        private void OnStateChanged(ProcessInfo process, ProcessState state)
        {
            if (process.IsLocal)
            {
                if (_isShutingDown)
                    return;
                if (state == ProcessState.Conflicted)
                    process.Worker.Start();
                else if (state == ProcessState.Faulted)
                    ScheduleSelfMessage(10000, new ProcessManagerCluster.ProcessDelayed { Process = process });
            }
            else
            {
                switch (state)
                {
                    case ProcessState.Starting:
                    case ProcessState.Running:
                    case ProcessState.Faulted:
                    case ProcessState.Inactive:
                    case ProcessState.Conflicted:
                        Broadcast(new ProcessManagerMessages.ProcessChange { ProcessName = process.Name, NewState = state });
                        break;
                }
            }
        }

        public void Handle(ProcessManagerCluster.ProcessSchedule message)
        {
            if (!_isLeader || _leader == null)
                return;

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
                if (process.IsLocal || process.State != GlobalProcessState.Offline)
                    continue;
                var foundNode = FindNodeForProcess(process, true, int.MaxValue);
                if (foundNode == null)
                    continue;
                process.AssignedNode = foundNode;
                foundNode.ProcessCount++;
                process.State = GlobalProcessState.ToBeStarted;
            }

            foreach (var process in _processes.Values)
            {
                if (process.IsLocal || process.State != GlobalProcessState.Online || process.AssignedNode == null)
                    continue;
                var foundNode = FindNodeForProcess(process, false, process.AssignedNode.ProcessCount - 2);
                if (foundNode == null)
                    continue;
                process.AssignedNode.ProcessCount--;
                process.AssignedNode = foundNode;
                foundNode.ProcessCount++;
                process.State = GlobalProcessState.ToBeStopped;
            }

            foreach (var process in _processes.Values)
            {
                if (process.State == GlobalProcessState.ToBeStarted)
                    Broadcast(new ProcessManagerMessages.ProcessStart { ProcessName = process.Name, AssignedNode = process.AssignedNode.NodeId });
                else if (process.State == GlobalProcessState.ToBeStopped)
                    Broadcast(new ProcessManagerMessages.ProcessStop { ProcessName = process.Name, AssignedNode = process.AssignedNode.NodeId });
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
    }
}
