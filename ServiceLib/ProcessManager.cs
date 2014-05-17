using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }

    public enum ProcessState
    {
        Uninitialized,
        Inactive,
        Starting,
        Running,
        Pausing,
        Stopping,
        Faulted
    }

    public interface IProcessWorker : IDisposable
    {
        ProcessState State { get; }
        void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler);
        void Start();
        void Pause();
        void Stop();
    }
}
