using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventProcessTrackSource
    {
        string TrackingId { get; }
        void AddEvent(EventStoreToken token);
        void CommitToTracker();
    }
    public interface IEventProcessTrackItem
    {
        string TrackingId { get; }
        Task<bool> WaitForFinish(int timeoutMilliseconds);
    }
    public interface IEventProcessTrackTarget
    {
        string HandlerName { get; }
        void ReportProgress(EventStoreToken token);
    }
    public interface IEventProcessTrackCoordinator
    {
        IEventProcessTrackSource CreateTracker();
        IEventProcessTrackItem FindTracker(string trackingId);
        IEventProcessTrackTarget RegisterHandler(string handlerName);
    }
    public class EventProcessTracking : IProcessWorker, IEventProcessTrackCoordinator
    {
        private readonly object _lock;
        private readonly ITime _time;
        private readonly Dictionary<string, TrackItem> _trackersById;
        private readonly Dictionary<string, TrackTarget> _handlers;
        private readonly List<TrackItem> _items;

        private readonly Task<bool> _finishedWait;
        private readonly Task<bool> _unfinishedWait;
        private List<TrackWaiter> _waiters;
        private long _fullMask;
        private int _nextSetIndex;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private TaskScheduler _scheduler;
        private CancellationToken _cancel;
        private CancellationTokenSource _cancelSource;

        public EventProcessTracking(ITime time)
        {
            _time = time;

            _lock = new object();
            _trackersById = new Dictionary<string, TrackItem>();
            _handlers = new Dictionary<string, TrackTarget>();
            _items = new List<TrackItem>();

            _finishedWait = TaskUtils.FromResult(true);
            _unfinishedWait = TaskUtils.FromResult(false);
            _fullMask = 0;
            _nextSetIndex = 0;
            _waiters = new List<TrackWaiter>();
        }

        private class TrackSource : IEventProcessTrackSource
        {
            public readonly EventProcessTracking Parent;
            public readonly string TrackingId;
            public EventStoreToken LastToken;

            public TrackSource(EventProcessTracking parent, string trackingId)
            {
                Parent = parent;
                TrackingId = trackingId;
                LastToken = EventStoreToken.Initial;
            }

            string IEventProcessTrackSource.TrackingId { get { return TrackingId; } }

            public void AddEvent(EventStoreToken token)
            {
                if (LastToken == null)
                    LastToken = token;
                else if (EventStoreToken.Compare(token, LastToken) > 0)
                    LastToken = token;
            }

            public void CommitToTracker()
            {
                var trackItem = new TrackItem(Parent, TrackingId, LastToken);
                lock (Parent._lock)
                {
                    Parent._trackersById.Add(TrackingId, trackItem);
                    Parent._items.Add(trackItem);
                }
            }
        }
        private class TrackItem : IEventProcessTrackItem
        {
            public readonly EventProcessTracking Parent;
            public readonly string TrackingId;
            public readonly EventStoreToken LastToken;
            public long UnfinishedHandlersSet;
            public readonly List<TrackWaiter> Waiters;

            public TrackItem(EventProcessTracking parent, string trackingId, EventStoreToken lastToken)
            {
                Parent = parent;
                TrackingId = trackingId;
                LastToken = lastToken;
                UnfinishedHandlersSet = parent._fullMask;
                Waiters = new List<TrackWaiter>();
            }

            string IEventProcessTrackItem.TrackingId { get { return TrackingId; } }

            public Task<bool> WaitForFinish(int timeoutMilliseconds)
            {
                if (UnfinishedHandlersSet == 0)
                    return Parent._finishedWait;
                if (timeoutMilliseconds <= 0)
                    return Parent._unfinishedWait;
                var maxTime = Parent._time.GetUtcTime().AddMilliseconds(timeoutMilliseconds);
                var waiter = new TrackWaiter(maxTime);
                lock (Parent._lock)
                {
                    if (UnfinishedHandlersSet == 0)
                        return Parent._finishedWait;
                    Waiters.Add(waiter);
                    Parent._waiters.Add(waiter);
                    return waiter.Task.Task;
                }
            }

            public void NotifyWaiters()
            {
                var result = UnfinishedHandlersSet == 0;
                foreach (var waiter in Waiters)
                {
                    waiter.Task.TrySetResult(result);
                }
            }
        }
        private class TrackTarget : IEventProcessTrackTarget
        {
            public readonly EventProcessTracking Parent;
            public readonly string HandlerName;
            public readonly long SetMask;

            public TrackTarget(EventProcessTracking parent, string name, long setMask)
            {
                Parent = parent;
                HandlerName = name;
                SetMask = setMask;
            }

            string IEventProcessTrackTarget.HandlerName { get { return HandlerName; } }

            public void ReportProgress(EventStoreToken token)
            {
                lock (Parent._lock)
                {
                    foreach (var item in Parent._items)
                    {
                        if ((item.UnfinishedHandlersSet & SetMask) == 0)
                            continue;
                        if (EventStoreToken.Compare(token, item.LastToken) >= 0)
                        {
                            item.UnfinishedHandlersSet &= ~SetMask;
                            if (item.UnfinishedHandlersSet == 0)
                                item.NotifyWaiters();
                        }
                    }
                }
            }
        }
        private class TrackWaiter
        {
            public readonly TaskCompletionSource<bool> Task;
            public readonly DateTime MaxTime;

            public TrackWaiter(DateTime maxTime)
            {
                Task = new TaskCompletionSource<bool>();
                MaxTime = maxTime;
            }
        }

        public IEventProcessTrackSource CreateTracker()
        {
            var trackingId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var source = new TrackSource(this, trackingId);
            return source;
        }

        public IEventProcessTrackItem FindTracker(string trackingId)
        {
            lock (_lock)
            {
                TrackItem trackItem;
                if (_trackersById.TryGetValue(trackingId, out trackItem))
                {
                    return trackItem;
                }
                else
                {
                    trackItem = new TrackItem(this, trackingId, EventStoreToken.Initial);
                    trackItem.UnfinishedHandlersSet = 0;
                    return trackItem;
                }
            }
        }

        public IEventProcessTrackTarget RegisterHandler(string handlerName)
        {
            lock (_lock)
            {
                TrackTarget target;
                if (_handlers.TryGetValue(handlerName, out target))
                    return target;
                var setMask = 1L << _nextSetIndex;
                target = new TrackTarget(this, handlerName, setMask);
                _fullMask |= setMask;
                _nextSetIndex++;
                _handlers.Add(handlerName, target);
                return target;
            }
        }

        public ProcessState State
        {
            get { return _processState; }
        }

        public void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler)
        {
            _onStateChanged = onStateChanged;
            _scheduler = scheduler;
        }

        public void Start()
        {
            _cancelSource = new CancellationTokenSource();
            _cancel = _cancelSource.Token;
            _time.Delay(1000, _cancel).ContinueWith(ProcessTimeouts, CancellationToken.None, TaskContinuationOptions.None, _scheduler);
            SetProcessState(ProcessState.Running);
        }

        private void ProcessTimeouts(Task timerTask)
        {
            if (timerTask.IsCanceled || _cancel.IsCancellationRequested)
            {
                lock (_lock)
                {
                    _waiters.ForEach(w => w.Task.TrySetResult(false));
                    _waiters.Clear();
                }
                SetProcessState(ProcessState.Inactive);
            }
            else
            {
                lock (_lock)
                {
                    foreach (var item in _items)
                    {
                        if (item.UnfinishedHandlersSet == 0)
                            item.NotifyWaiters();
                    }
                    var time = _time.GetUtcTime();
                    _items.RemoveAll(i => i.UnfinishedHandlersSet == 0);
                    foreach (var waiter in _waiters)
                    {
                        if (waiter.MaxTime > time)
                            continue;
                        waiter.Task.TrySetResult(false);
                    }
                    _waiters.RemoveAll(w => w.MaxTime > time);
                }
                _time.Delay(1000, _cancel).ContinueWith(ProcessTimeouts);
            }
        }

        private void SetProcessState(ProcessState processState)
        {
            _processState = processState;
            var onStateChanged = _onStateChanged;
            if (onStateChanged != null)
                onStateChanged(processState);
        }

        public void Pause()
        {
            Stop();
        }

        public void Stop()
        {
            _cancelSource.Cancel();
            SetProcessState(ProcessState.Stopping);
        }

        public void Dispose()
        {
            if (_cancelSource != null)
                _cancelSource.Dispose();
        }
    }
}
