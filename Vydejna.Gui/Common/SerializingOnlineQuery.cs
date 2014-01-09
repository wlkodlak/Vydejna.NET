using ServiceLib;
using System;
using System.Threading.Tasks;

namespace Vydejna.Gui.Common
{
    public class SerializingOnlineQuery<T> where T : class
    {
        private Func<T, bool> _immediateFunc;
        private Action<T, Action> _onlineFunc;
        private Action<T> _reportFunc;
        private bool _cancelMode;
        private QueryInfo _current, _waiting;
        private object _lock = new object();

        private class QueryInfo
        {
            public T State;
            public bool Cancelled;
            public Action OnComplete;
        }

        public SerializingOnlineQuery(
            Func<T, bool> immediateFunc,
            Action<T, Action> onlineFunc,
            Action<T> reportFunc,
            bool cancelMode)
        {
            _immediateFunc = immediateFunc;
            _onlineFunc = onlineFunc;
            _reportFunc = reportFunc;
            _cancelMode = cancelMode;
        }

        public void Run(T state, Action onComplete)
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
                    finishTask.OnComplete();
                onComplete();
            }
            else
            {
                var info = new QueryInfo
                {
                    State = state,
                    OnComplete = onComplete
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
                    finishTask.OnComplete();
                if (runOnline)
                {
                    _onlineFunc(state, FinishOnlineRun);
                }
            }
        }

        private void FinishOnlineRun()
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
            originalCurrent.OnComplete();
            if (stateForOnline != null)
            {
                _onlineFunc(_current.State, FinishOnlineRun);
            }
        }
    }
}
