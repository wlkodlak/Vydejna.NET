using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public class EventProcessSimple
        : IProcessWorker
    {
        private readonly EventProcessSimpleTraceSource _logger;
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly IEventSubscriptionManager _subscriptions;
        private readonly IEventProcessTrackTarget _tracker;
        private readonly object _lock;
        private readonly Stopwatch _stopwatch;
        private readonly string _processName;
        private CancellationTokenSource _cancelPause, _cancelStop;
        private CancellationToken _cancelPauseToken, _cancelStopToken;
        private ProcessState _processState;
        private Action<ProcessState> _onProcessStateChanged;
        private readonly ITime _time;
        private Task _processTask;
        private TaskScheduler _scheduler;

        public EventProcessSimple(
            IMetadataInstance metadata, IEventStreamingDeserialized streaming,
            IEventSubscriptionManager subscriptions, ITime time, IEventProcessTrackTarget tracker)
        {
            _metadata = metadata;
            _streaming = streaming;
            _tracker = tracker;
            _subscriptions = subscriptions;
            _processState = ProcessState.Uninitialized;
            _time = time;
            _processName = _metadata.ProcessName;
            _stopwatch = new Stopwatch();
            _lock = new object();
            _logger = new EventProcessSimpleTraceSource("ServiceLib.EventProcessSimple." + _processName);
        }

        public EventProcessSimple Register<T>(IProcessEvent<T> handler)
        {
            _subscriptions.Register(handler);
            return this;
        }

        public EventProcessSimple WithTokenFlushing(int flushAfter)
        {
            return this;
        }

        public ProcessState State
        {
            get
            {
                lock (_lock)
                    return _processState;
            }
        }

        public void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler)
        {
            _processState = ProcessState.Inactive;
            _onProcessStateChanged = onStateChanged;
            _scheduler = scheduler;
        }

        public void Start()
        {
            _cancelPause = new CancellationTokenSource();
            _cancelPauseToken = _cancelPause.Token;
            _cancelStop = new CancellationTokenSource();
            _cancelStopToken = _cancelStop.Token;
            SetProcessState(ProcessState.Starting);
            new Task(() => _processTask = ProcessCore()).RunSynchronously(_scheduler);
        }

        public void Pause()
        {
            SetProcessState(ProcessState.Pausing);
            if (_cancelPause != null)
                _cancelPause.Cancel();
            _streaming.Close();
        }

        public void Stop()
        {
            SetProcessState(ProcessState.Stopping);
            if (_cancelPause != null)
                _cancelPause.Cancel();
            if (_cancelStop != null)
                _cancelStop.Cancel();
            _streaming.Close();
            _processTask.Wait(1000);
        }

        public void Dispose()
        {
            if (_cancelPause != null)
                _cancelPause.Cancel();
            if (_cancelStop != null)
                _cancelStop.Cancel();
            if (_cancelPause != null)
                _cancelPause.Dispose();
            if (_cancelStop != null)
                _cancelStop.Dispose();
            _processTask.Wait(1000);
            SetProcessState(ProcessState.Inactive);
        }

        private void SetProcessState(ProcessState newState)
        {
            Action<ProcessState> handler;
            lock (_lock)
            {
                if (_processState == newState)
                    return;
                if (_processState == ProcessState.Inactive &&
                    (newState == ProcessState.Pausing || newState == ProcessState.Stopping))
                    return;
                _processState = newState;
                handler = _onProcessStateChanged;
            }
            try
            {
                if (handler != null)
                    handler(newState);
            }
            catch (Exception exception)
            {
                _logger.SetProcessStateCallbackFailed(_processName, exception);
            }
        }

        private EventStoreToken _lastToken;

        private async Task ProcessCore()
        {
            try
            {
                try
                {
                    await Initialize();

                    var firstIteration = true;
                    _lastToken = null;

                    while (!_cancelPauseToken.IsCancellationRequested)
                    {
                        var nowait = firstIteration || _lastToken != null;
                        firstIteration = false;
                        var nextEvent =
                            await TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _time, _cancelPauseToken);
                        var tokenToSave = await ProcessEvent(nextEvent);
                        await SaveToken(tokenToSave);
                    }
                }
                finally
                {
                    _stopwatch.Stop();
                    _streaming.Close();
                    _logger.ProcessingEnded(_processName);
                }
                SetProcessState(ProcessState.Inactive);
            }
            catch (OperationCanceledException)
            {
                SetProcessState(ProcessState.Inactive);
            }
            catch
            {
                SetProcessState(ProcessState.Faulted);
            }
        }

        private async Task Initialize()
        {
            _stopwatch.Start();
            var token = await TaskUtils.Retry(() => _metadata.GetToken(), _time, _cancelPauseToken);
            _logger.ReceivedInitialToken(_processName, token);

            SetProcessState(ProcessState.Running);
            var handledTypes = _subscriptions.GetHandledTypes();
            _streaming.Setup(token, handledTypes, _metadata.ProcessName);
            _logger.HandledTypesSetup(_processName, handledTypes);

            _logger.InitializationFinished(_processName, _stopwatch.ElapsedMilliseconds);
            _stopwatch.Restart();
        }

        private async Task<EventStoreToken> ProcessEvent(EventStreamingDeserializedEvent nextEvent)
        {
            var tokenToSave = (EventStoreToken) null;
            if (nextEvent == null)
            {
                tokenToSave = _lastToken;
                _lastToken = null;
                _logger.NoEventsAvailable(_processName, _stopwatch.ElapsedMilliseconds);
                _stopwatch.Restart();
            }
            else if (nextEvent.Event != null)
            {
                _lastToken = nextEvent.Token;
                var eventType = nextEvent.Event.GetType();
                var handler = _subscriptions.FindHandler(eventType);
                _logger.ReceivedEvent(_processName, eventType, nextEvent, _stopwatch.ElapsedMilliseconds);
                _stopwatch.Restart();

                var eventHandlerError = await TryHandleEvent(handler, nextEvent, eventType);
                tokenToSave = _lastToken;
                if (eventHandlerError != null)
                {
                    _stopwatch.Restart();
                    await MarkAsDeadLetter(nextEvent);
                }
            }
            return tokenToSave;
        }

        private async Task<Exception> TryHandleEvent(IEventSubscription handler, EventStreamingDeserializedEvent nextEvent, Type eventType)
        {
            Exception eventHandlerError;
            try
            {
                await TaskUtils.Retry(() => handler.Handle(nextEvent.Event), _time, _cancelStopToken, 3);

                _tracker.ReportProgress(nextEvent.Token);
                _logger.EventProcessed(_processName, eventType, nextEvent, _stopwatch.ElapsedMilliseconds);
                _stopwatch.Restart();
                eventHandlerError = null;
            }
            catch (Exception exception)
            {
                _logger.EventHandlerFailed(
                    _processName, eventType, nextEvent, _stopwatch.ElapsedMilliseconds, exception);
                eventHandlerError = exception;
            }
            return eventHandlerError;
        }

        private async Task MarkAsDeadLetter(EventStreamingDeserializedEvent nextEvent)
        {
            while (true)
            {
                _cancelStopToken.ThrowIfCancellationRequested();
                try
                {
                    await _streaming.MarkAsDeadLetter();
                    _logger.EventMarkedAsDeadLetter(_processName, nextEvent, _stopwatch.ElapsedMilliseconds);
                    _stopwatch.Restart();
                    break;
                }
                catch (Exception exception)
                {
                    _logger.MarkingAsDeadLetterFailed(_processName, nextEvent, exception);
                }
            }
        }

        private async Task SaveToken(EventStoreToken tokenToSave)
        {
            if (tokenToSave != null)
            {
                while (!_cancelStop.IsCancellationRequested)
                {
                    try
                    {
                        await _metadata.SetToken(tokenToSave);
                        _logger.UpdatedTokenInMetadata(_processName, tokenToSave, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();
                        break;
                    }
                    catch (Exception exception)
                    {
                        _logger.UpdatingTokenInMetadataFailed(_processName, tokenToSave, exception);
                    }
                }
            }
        }
    }

    public class EventProcessSimpleTraceSource : TraceSource
    {
        public EventProcessSimpleTraceSource(string name)
            : base(name)
        {
        }

        public void ReceivedInitialToken(string processName, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Process will start at token {Token}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void HandledTypesSetup(string processName, IEnumerable<Type> handledTypes)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "Process registered handled types");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("HandledTypes", false, string.Join(", ", handledTypes.Select(x => x.Name)));
            msg.Log(this);
        }

        public void InitializationFinished(string processName, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 9, "Initialization finished");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void NoEventsAvailable(string processName, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 20, "No more events available at the moment");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void SetProcessStateCallbackFailed(string processName, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 90, "No more events available at the moment");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ReceivedEvent(string processName, Type eventType, EventStreamingDeserializedEvent nextEvent, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 21, "Received event {EventType}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventType", false, eventType.Name);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void EventProcessed(string processName, Type eventType, EventStreamingDeserializedEvent nextEvent, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 22, "Event {EventType} processed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventType", false, eventType.Name);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void EventHandlerFailed(string processName, Type eventType, EventStreamingDeserializedEvent nextEvent, long elapsedMilliseconds, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 24, "Event handler for {EventType} failed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventType", false, eventType);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void EventMarkedAsDeadLetter(string processName, EventStreamingDeserializedEvent nextEvent, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 30, "Event {Token} marked as dead letter");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void MarkingAsDeadLetterFailed(string processName, EventStreamingDeserializedEvent nextEvent, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 32, "Event {Token} could not be marked as dead letter");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void UpdatedTokenInMetadata(string processName, EventStoreToken tokenToSave, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 42, "Token {Token} was saved");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, tokenToSave);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void UpdatingTokenInMetadataFailed(string processName, EventStoreToken tokenToSave, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 42, "Token {Token} could not be saved");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, tokenToSave);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void ProcessingEnded(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 50, "Processing ended");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }
    }
}