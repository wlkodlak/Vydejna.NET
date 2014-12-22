using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IProcessManager
    {
        void RegisterLocal(string name, IProcessWorker worker);
        void RegisterGlobal(string name, IProcessWorker worker, int processingCost, int transitionCost);
        void RegisterBus(string name, IBus bus, IProcessWorker worker);
        void Start();
        void Stop();
        void WaitForStop();
        List<ProcessManagerMessages.InfoProcess> GetLocalProcesses();
        ProcessManagerMessages.InfoGlobal GetGlobalInfo();
        List<ProcessManagerMessages.InfoProcess> GetLeaderProcesses();
    }

    public enum ProcessState
    {
        Uninitialized,
        Inactive,
        Starting,
        Running,
        Pausing,
        Stopping,
        Conflicted,
        Faulted,
        Unsupported
    }

    public interface IProcessWorker : IDisposable
    {
        ProcessState State { get; }
        void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler);
        void Start();
        void Pause();
        void Stop();
    }

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

        public class Heartbeat : GenericMessage
        {
        }

        public class HeartStopped : GenericMessage
        {
        }

        public class ProcessMessage : GenericMessage
        {
            public string AssignedNode { get; set; }
            public string ProcessName { get; set; }
        }

        public class ProcessStart : ProcessMessage
        {
        }

        public class ProcessStop : ProcessMessage
        {
        }

        public class ProcessChange : GenericMessage
        {
            public string ProcessName { get; set; }
            public ProcessState NewState { get; set; }
        }

        public class ConnectionRestored
        {
        }

        public class ProcessRequest
        {
            public string ProcessName { get; set; }
            public bool ShouldBeOnline { get; set; }
        }

        public class InfoProcess
        {
            public string ProcessName, ProcessStatus, AssignedNode;
        }

        public class InfoGlobal
        {
            public string NodeName, LeaderName;
            public bool IsConnected;
            public List<InfoProcess> RunningProcesses;
        }
    }

    public enum GlobalProcessState
    {
        Offline,
        Starting,
        Online,
        Stopping,
        ToBeStarted,
        ToBeStopped,
        Unsupported
    }
}