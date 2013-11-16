using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Gui.Common
{
    public class SerializingOnlineQuery<T> where T : class
    {
        private Func<T, bool> _immediateFunc;
        private Func<T, Task> _onlineFunc;
        private Action<T> _reportFunc;
        private bool _cancelMode;
        private QueryInfo _current, _waiting;
        private object _lock = new object();

        private class QueryInfo
        {
            public T State;
            public bool Cancelled;
            public TaskCompletionSource<object> Task;
        }

        public SerializingOnlineQuery(
            Func<T, bool> immediateFunc,
            Func<T, Task> onlineFunc,
            Action<T> reportFunc,
            bool cancelMode)
        {
            _immediateFunc = immediateFunc;
            _onlineFunc = onlineFunc;
            _reportFunc = reportFunc;
            _cancelMode = cancelMode;
        }

        public Task Run(T state)
        {
            if (_immediateFunc(state))
            {
                QueryInfo finishTask = null;
                lock (_lock)
                {
                    finishTask = _waiting;
                    if (_current != null)
                        _current.Cancelled = true;
                    _waiting = null;
                }
                _reportFunc(state);
                if (finishTask != null)
                    finishTask.Task.SetResult(null);
                return TaskResult.GetCompletedTask();
            }
            else
            {
                var info = new QueryInfo
                {
                    State = state,
                    Task = new TaskCompletionSource<object>()
                };
                bool runOnline;
                QueryInfo finishTask = null;
                lock (_lock)
                {
                    if (_current == null)
                    {
                        _current = info;
                        runOnline = true;
                    }
                    else
                    {
                        finishTask = _waiting;
                        _current.Cancelled = true;
                        _waiting = info;
                        runOnline = false;
                    }
                }
                if (finishTask != null)
                    finishTask.Task.SetResult(null);
                if (runOnline)
                {
                    var task = _onlineFunc(state);
                    task.ContinueWith(FinishOnlineRun);
                }
                return info.Task.Task;
            }
        }

        private void FinishOnlineRun(Task x)
        {
            bool reportCurrent;
            T stateForOnline;
            QueryInfo originalCurrent;

            lock (_lock)
            {
                reportCurrent = _waiting == null && !_current.Cancelled;
                originalCurrent = _current;
                if (_waiting != null)
                {
                    _current = _waiting;
                    _waiting = null;
                    stateForOnline = _current.State;
                }
                else
                    stateForOnline = null;
            }
            if (reportCurrent)
                _reportFunc(originalCurrent.State);
            originalCurrent.Task.SetResult(null);
            if (stateForOnline != null)
            {
                var task = _onlineFunc(_current.State);
                task.ContinueWith(FinishOnlineRun);
            }
        }
    }
}
