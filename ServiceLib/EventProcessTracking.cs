using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
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
        private readonly List<TrackWaiter> _waiters;
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
            private readonly EventProcessTracking _parent;
            private readonly string _trackingId;
            private EventStoreToken _lastToken;

            public TrackSource(EventProcessTracking parent, string trackingId)
            {
                _parent = parent;
                _trackingId = trackingId;
                _lastToken = EventStoreToken.Initial;
            }

            string IEventProcessTrackSource.TrackingId
            {
                get { return _trackingId; }
            }

            public void AddEvent(EventStoreToken token)
            {
                if (_lastToken == null)
                    _lastToken = token;
                else if (EventStoreToken.Compare(token, _lastToken) > 0)
                    _lastToken = token;
            }

            public void CommitToTracker()
            {
                var trackItem = new TrackItem(_parent, _trackingId, _lastToken);
                lock (_parent._lock)
                {
                    _parent._trackersById.Add(_trackingId, trackItem);
                    _parent._items.Add(trackItem);
                    long alreadyFinished = 0;
                    foreach (var handler in _parent._handlers.Values)
                    {
                        if (EventStoreToken.Compare(handler.LastToken, _lastToken) >= 0)
                            alreadyFinished |= handler.SetMask;
                    }
                    trackItem.UnfinishedHandlersSet &= ~alreadyFinished;
                }
            }
        }

        private class TrackItem : IEventProcessTrackItem
        {
            private readonly EventProcessTracking _parent;
            private readonly string _trackingId;
            private readonly List<TrackWaiter> _waiters;
            public readonly EventStoreToken LastToken;
            public long UnfinishedHandlersSet;

            public TrackItem(EventProcessTracking parent, string trackingId, EventStoreToken lastToken)
            {
                _parent = parent;
                _trackingId = trackingId;
                _waiters = new List<TrackWaiter>();
                LastToken = lastToken;
                UnfinishedHandlersSet = parent._fullMask;
            }

            string IEventProcessTrackItem.TrackingId
            {
                get { return _trackingId; }
            }

            public Task<bool> WaitForFinish(int timeoutMilliseconds)
            {
                if (UnfinishedHandlersSet == 0)
                    return _parent._finishedWait;
                if (timeoutMilliseconds <= 0)
                    return _parent._unfinishedWait;
                var maxTime = _parent._time.GetUtcTime().AddMilliseconds(timeoutMilliseconds);
                var waiter = new TrackWaiter(maxTime);
                lock (_parent._lock)
                {
                    if (UnfinishedHandlersSet == 0)
                        return _parent._finishedWait;
                    _waiters.Add(waiter);
                    _parent._waiters.Add(waiter);
                    return waiter.Task.Task;
                }
            }

            public void NotifyWaiters()
            {
                var result = UnfinishedHandlersSet == 0;
                foreach (var waiter in _waiters)
                {
                    waiter.Task.TrySetResult(result);
                }
            }
        }

        private class TrackTarget : IEventProcessTrackTarget
        {
            private readonly EventProcessTracking _parent;
            private readonly string _handlerName;
            public readonly long SetMask;
            public EventStoreToken LastToken;

            public TrackTarget(EventProcessTracking parent, string name, long setMask)
            {
                _parent = parent;
                _handlerName = name;
                SetMask = setMask;
                LastToken = EventStoreToken.Initial;
            }

            string IEventProcessTrackTarget.HandlerName
            {
                get { return _handlerName; }
            }

            public void ReportProgress(EventStoreToken token)
            {
                lock (_parent._lock)
                {
                    LastToken = token;
                    foreach (var item in _parent._items)
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
            _time.Delay(1000, _cancel)
                .ContinueWith(ProcessTimeouts, CancellationToken.None, TaskContinuationOptions.None, _scheduler);
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
                _time.Delay(1000, _cancel).ContinueWith(ProcessTimeouts, _cancel);
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

    public class EventProcessTrackService
    {
        private readonly IEventProcessTrackCoordinator _coordinator;

        private static readonly EventProcessTrackingTraceSource Logger =
            new EventProcessTrackingTraceSource("ServiceLib.EventProcessTracking");

        public EventProcessTrackService(IEventProcessTrackCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public void Register(IHttpRouteCommonConfigurator config)
        {
            config.Route("utils/tracking/{id}").To(HandleTracking);
        }

        public static string GetTrackingUrlBase(string appBase)
        {
            return string.Concat(appBase, "utils/tracking/");
        }

        public async Task HandleTracking(IHttpServerStagedContext ctx)
        {
            // utils/tracking
            var trackingId = ctx.Route("id").AsString().Get();
            var timeout = ctx.Parameter("timeout").AsInteger().Default(10000).Get();
            var finished = await _coordinator.FindTracker(trackingId).WaitForFinish(timeout);

            ctx.StatusCode = finished ? (int) HttpStatusCode.OK : (int) HttpStatusCode.Accepted;
            Logger.TrackingReport(trackingId, timeout, finished);
        }
    }

    public class EventProcessTrackingTraceSource : TraceSource
    {
        public EventProcessTrackingTraceSource(string name)
            : base(name)
        {
        }

        public void TrackingReport(string trackingId, int timeout, bool finished)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 1,
                "Tracking request with id {TrackingId} reported " + (finished ? "" : "un") + "finished command");
            msg.SetProperty("TrackingId", false, trackingId);
            msg.SetProperty("Timeout", false, timeout);
            msg.SetProperty("Finished", false, finished);
            msg.Log(this);
        }
    }
}