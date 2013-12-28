using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class EventStreamingFilter
    {
        private readonly EventStoreToken _firstToken;
        private readonly IList<string> _types;
        private readonly IList<string> _prefixes;

        public EventStoreToken FirstToken { get { return _firstToken; } }
        public IList<string> Types { get { return _types; } }
        public IList<string> StreamPrefixes { get { return _prefixes; } }

        public EventStreamingFilter(EventStoreToken firstToken, IList<string> types, IList<string> prefixes)
        {
            _firstToken = firstToken;
            _types = types;
            _prefixes = prefixes;
        }
    }

    public interface IEventStreaming
    {
        IEventStreamer GetStreamer(EventStreamingFilter filter);
    }
    public interface IEventStreamer : IDisposable
    {
        void GetNextEvent(Action<EventStoreEvent> onComplete, CancellationToken cancel, bool withoutWaiting);
    }
    public interface IEventStreamingDeserialized
    {
        void Setup(EventStoreToken firstToken, IList<Type> types, IList<string> prefixes, bool prefilterType);
        void GetNextEvent(Action<EventStoreToken, object> onEventRead, Action onEventNotAvailable, Action<Exception, EventStoreEvent> onError, CancellationToken cancel, bool nowait);
    }

    public class EventStreaming : IEventStreaming
    {
        private IEventStoreWaitable _store;
        private IQueueExecution _executor;

        public EventStreaming(IEventStoreWaitable store, IQueueExecution executor)
        {
            _store = store;
            _executor = executor;
        }

        public IEventStreamer GetStreamer(EventStreamingFilter filter)
        {
            return new EventsStream(this, filter);
        }

        private class EventsStream : IEventStreamer
        {
            private IEventStoreWaitable _store;
            private IQueueExecution _executor;
            private IList<SubStreamer> _substreamers;
            private object _lock;
            private CancellationTokenRegistration _cancel;
            private Action<EventStoreEvent> _onComplete;
            private bool _nowait;
            private bool _isWorking;

            private class GetNextEventCompleted : IQueuedExecutionDispatcher
            {
                private Action<EventStoreEvent> _onComplete;
                private EventStoreEvent _completedEvent;

                public GetNextEventCompleted(Action<EventStoreEvent> onComplete, EventStoreEvent completedEvent)
                {
                    _onComplete = onComplete;
                    _completedEvent = completedEvent;
                }

                public void Execute()
                {
                    _onComplete(_completedEvent);
                }
            }

            public EventsStream(EventStreaming parent, EventStreamingFilter filter)
            {
                _store = parent._store;
                _executor = parent._executor;
                _lock = new object();
                if (filter.StreamPrefixes.Count == 0)
                {
                    if (filter.Types.Count == 0)
                        _substreamers = new SubStreamer[1] { new SubStreamer(this, filter.FirstToken, null, null) };
                    else
                        _substreamers = filter.Types.Select(type => new SubStreamer(this, filter.FirstToken, null, type)).ToArray();
                }
                else
                {
                    if (filter.Types.Count == 0)
                        _substreamers = filter.StreamPrefixes.Select(prefix => new SubStreamer(this, filter.FirstToken, prefix, null)).ToArray();
                    else
                        _substreamers = filter.StreamPrefixes.SelectMany(prefix => filter.Types.Select(type => new SubStreamer(this, filter.FirstToken, prefix, type))).ToArray();
                }
            }

            public void GetNextEvent(Action<EventStoreEvent> onComplete, CancellationToken cancel, bool withoutWaiting)
            {
                lock (_lock)
                {
                    try
                    {
                        _isWorking = true;
                        _cancel = cancel.Register(OnCancelled);
                        if (cancel.IsCancellationRequested)
                            return;
                        _onComplete = onComplete;
                        _nowait = withoutWaiting;
                        foreach (var streamer in _substreamers)
                            streamer.StartLoading();
                    }
                    finally
                    {
                        _isWorking = false;
                    }
                }
                OnPotentiallyCompleted();
            }

            private void OnCancelled()
            {
                Action<EventStoreEvent> onComplete;
                lock (_lock)
                {
                    if (_isWorking)
                        return;
                    onComplete = _onComplete;
                    _onComplete = null;
                    foreach (var streamer in _substreamers)
                        streamer.Cancel();
                }
                _executor.Enqueue(new GetNextEventCompleted(onComplete, null));
            }

            private void OnPotentiallyCompleted()
            {
                bool hasCompleted = false;
                Action<EventStoreEvent> onComplete = null;
                EventStoreEvent completedEvent = null;
                SubStreamer selectedStreamer = null;
                lock (_lock)
                {
                    onComplete = _onComplete;
                    if (_onComplete == null)
                        return;
                    var allWaiting = true;
                    var anyLoading = false;
                    foreach (var streamer in _substreamers)
                    {
                        allWaiting = allWaiting && streamer.IsWaiting;
                        anyLoading = anyLoading || streamer.IsLoading;
                        if (!streamer.IsReady)
                            continue;
                        var evt = streamer.AvailableEvent;
                        if (completedEvent == null || EventStoreToken.Compare(completedEvent.Token, evt.Token) > 1)
                        {
                            selectedStreamer = streamer;
                            completedEvent = evt;
                        }
                    }
                    if (allWaiting && _nowait)
                    {
                        hasCompleted = true;
                        _cancel.Dispose();
                        _onComplete = null;
                    }
                    else if (!anyLoading && completedEvent != null)
                    {
                        hasCompleted = true;
                        _onComplete = null;
                        _cancel.Dispose();
                        selectedStreamer.ReportEventUsed();
                    }
                }
                if (hasCompleted)
                    _executor.Enqueue(new GetNextEventCompleted(onComplete, completedEvent));
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_onComplete != null)
                    {
                        _onComplete = null;
                        _cancel.Dispose();
                    }
                    foreach (var streamer in _substreamers)
                        streamer.Cancel();
                }
            }

            private enum StreamerStatus { Initial, Ready, Loading, Waiting, Failed }

            private class SubStreamer
            {
                private StreamerStatus _status;
                private EventsStream _parent;
                private EventStoreToken _token;
                private string _prefix;
                private string _eventType;
                private Queue<EventStoreEvent> _readyEvents;
                private EventStoreEvent _firstEvent;
                private IDisposable _currentWait;

                public SubStreamer(EventsStream parent, EventStoreToken token, string prefix, string eventType)
                {
                    _parent = parent;
                    _token = token;
                    _prefix = prefix;
                    _eventType = eventType;
                    _status = StreamerStatus.Initial;
                    _readyEvents = new Queue<EventStoreEvent>();
                    _firstEvent = null;
                }

                public bool IsWaiting { get { return _status == StreamerStatus.Waiting; } }
                public bool IsLoading { get { return _status == StreamerStatus.Loading || _status == StreamerStatus.Failed; } }
                public bool IsReady { get { return _status == StreamerStatus.Ready; } }
                public EventStoreEvent AvailableEvent { get { return _firstEvent; } }

                public void StartLoading()
                {
                    if (_status == StreamerStatus.Initial || _status == StreamerStatus.Failed)
                    {
                        _status = StreamerStatus.Loading;
                        _parent._store.GetAllEvents(_token, _prefix, _eventType, 20, true, LoadCompleted, LoadFailed);
                    }
                }

                public void ReportEventUsed()
                {
                    if (_status == StreamerStatus.Ready)
                    {
                        if (_readyEvents.Count > 0)
                            _firstEvent = _readyEvents.Dequeue();
                        else
                        {
                            _firstEvent = null;
                            _status = StreamerStatus.Loading;
                            _parent._store.GetAllEvents(_token, _prefix, _eventType, 20, true, LoadCompleted, LoadFailed);
                        }
                    }
                }

                public void Cancel()
                {
                    if (_currentWait != null)
                        _currentWait.Dispose();
                }

                private void LoadCompleted(IEventStoreCollection events)
                {
                    lock (_parent._lock)
                    {
                        _currentWait = null;
                        foreach (var evt in events.Events)
                        {
                            if (_firstEvent == null)
                                _firstEvent = evt;
                            else
                                _readyEvents.Enqueue(evt);
                        }
                        _token = events.NextToken;
                        if (_firstEvent == null)
                        {
                            _status = StreamerStatus.Waiting;
                            _currentWait = _parent._store.WaitForEvents(_token, _prefix, _eventType, 20, true, LoadCompleted, LoadFailed);
                        }
                        else if (_status == StreamerStatus.Loading)
                        {
                            _status = StreamerStatus.Ready;
                            _parent.OnPotentiallyCompleted();
                        }
                    }
                }

                private void LoadFailed(Exception exception)
                {
                    lock (_parent._lock)
                    {
                        _currentWait = null;
                        _status = StreamerStatus.Failed;
                    }
                }
            }
        }
    }

    public class EventStreamingDeserialized : IEventStreamingDeserialized
    {
        private IEventStreaming _streaming;
        private IEventSourcedSerializer _serializer;
        private IEventStreamer _streamer;
        private HashSet<string> _typeFilter;
        private Action<EventStoreToken, object> _onEventRead;
        private Action _onEventNotAvailable;
        private Action<Exception, EventStoreEvent> _onError;
        private bool _nowait;
        private CancellationToken _cancel;

        public EventStreamingDeserialized(IEventStreaming streaming, IEventSourcedSerializer serializer)
        {
            _streaming = streaming;
            _serializer = serializer;
        }

        public void Setup(EventStoreToken firstToken, IList<Type> types, IList<string> prefixes, bool prefilterType)
        {
            _typeFilter = new HashSet<string>(types.Select(_serializer.GetTypeName));
            var typeNames = prefilterType ? _typeFilter.ToArray() : new string[0];
            _streamer = _streaming.GetStreamer(new EventStreamingFilter(firstToken, typeNames, prefixes));
        }

        public void GetNextEvent(Action<EventStoreToken, object> onEventRead, Action onEventNotAvailable, Action<Exception, EventStoreEvent> onError, CancellationToken cancel, bool nowait)
        {
            _onEventRead = onEventRead;
            _onEventNotAvailable = onEventNotAvailable;
            _onError = onError;
            _cancel = cancel;
            _nowait = nowait;
            _streamer.GetNextEvent(RawEventReceived, _cancel, _nowait);
        }

        private void RawEventReceived(EventStoreEvent rawEvent)
        {
            if (rawEvent == null)
                _onEventNotAvailable();
            else
            {
                if (_typeFilter.Contains(rawEvent.Type))
                {
                    object deserialized;
                    try
                    {
                        deserialized = _serializer.Deserialize(rawEvent);
                    }
                    catch (Exception exception)
                    {
                        _onError(exception, rawEvent);
                        return;
                    }
                    _onEventRead(rawEvent.Token, deserialized);
                }
                else
                    _streamer.GetNextEvent(RawEventReceived, _cancel, _nowait);
            }
        }
    }
}
