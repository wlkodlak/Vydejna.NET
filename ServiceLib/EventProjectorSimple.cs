using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventProjector
    {
        void Register<T>(IProcessEvent<T> handler);
    }

    public interface IEventProjection
        : IProcessEvent<ProjectorMessages.Reset>
            , IProcessEvent<ProjectorMessages.UpgradeFrom>
    {
        string GetVersion();
        EventProjectionUpgradeMode UpgradeMode(string storedVersion);
    }

    public enum EventProjectionUpgradeMode
    {
        NotNeeded,
        Rebuild,
        Upgrade
    }

    public static class ProjectorMessages
    {
        public class ConcurrencyException : Exception
        {
        }

        public class HandlerFailedException : Exception
        {
            public HandlerFailedException(Exception inner)
                : base("Handler crashed", inner)
            {
            }
        }

        public class RebuildFinished
        {
        }

        public class UpgradeFrom
        {
            private readonly string _version;

            public string Version
            {
                get { return _version; }
            }

            public UpgradeFrom(string version)
            {
                _version = version;
            }
        }

        public class Reset
        {
        }

        public class Flush
        {
        }

        public class Resume
        {
        }
    }

    public class EventProjectorSimple : IEventProjector, IProcessWorker
    {
        private readonly object _lock;
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly IEventSubscriptionManager _subscriptions;
        private readonly IEventProjection _projectionInfo;
        private readonly IEventProcessTrackTarget _tracker;
        private readonly ITime _time;
        private readonly string _processName;
        private readonly Stopwatch _stopwatch;
        private readonly EventProjectorSimpleTraceSource _logger;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private CancellationTokenSource _cancelPause, _cancelStop;
        private bool _useDeadLetters;
        private CancellationToken _cancelPauseToken;
        private CancellationToken _cancelStopToken;
        private TaskScheduler _scheduler;
        private int _processedEventsCount;
        private Task _processTask;
        private EventProjectionUpgradeMode _rebuildMode;
        private Stopwatch _rebuildStopwatch;
        private bool _needsFlush;
        private EventStoreToken _lastToken;
        private bool _firstIteration;

        public EventProjectorSimple(
            IEventProjection projection,
            IMetadataInstance metadata,
            IEventStreamingDeserialized streaming,
            IEventSubscriptionManager subscriptions,
            ITime time,
            IEventProcessTrackTarget tracker)
        {
            _lock = new object();
            _projectionInfo = projection;
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _time = time;
            _tracker = tracker;
            _processName = _metadata.ProcessName;
            _stopwatch = new Stopwatch();
            _logger = new EventProjectorSimpleTraceSource("ServiceLib.EventProjectorSimple." + _processName);
        }

        public void Register<T>(IProcessEvent<T> handler)
        {
            _subscriptions.Register(handler);
        }

        public EventProjectorSimple UseDeadLetters(bool enabled = true)
        {
            _useDeadLetters = enabled;
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
            _onStateChanged = onStateChanged;
            _scheduler = scheduler;
            SetProcessState(ProcessState.Inactive);
        }

        private void SetProcessState(ProcessState state)
        {
            Action<ProcessState> handler;
            lock (_lock)
            {
                if (_processState == state)
                    return;
                if (_processState == ProcessState.Inactive &&
                    (state == ProcessState.Pausing || state == ProcessState.Stopping))
                    return;
                _processState = state;
                handler = _onStateChanged;
            }
            if (handler != null)
            {
                try
                {
                    handler(state);
                }
                catch (Exception exception)
                {
                    _logger.SetProcessStateHandlerFailed(_processName, exception);
                }
            }
        }

        public void Start()
        {
            _cancelPause = new CancellationTokenSource();
            _cancelPauseToken = _cancelPause.Token;
            _cancelStop = new CancellationTokenSource();
            _cancelStopToken = _cancelStop.Token;
            SetProcessState(ProcessState.Starting);
            new Task(() => _processTask = ProjectorCore()).RunSynchronously(_scheduler);
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
            _processState = ProcessState.Uninitialized;
        }

        private async Task ProjectorCore()
        {
            try
            {
                await InitializeProjection();

                _firstIteration = true;
                _needsFlush = false;
                _lastToken = null;
                while (!_cancelPauseToken.IsCancellationRequested)
                {
                    _stopwatch.Restart();
                    var nextEvent = await GetNextEvent();

                    if (nextEvent == null)
                    {
                        _logger.NoEventsAvailable(_processName, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();
                        if (_rebuildMode == EventProjectionUpgradeMode.Rebuild)
                            await EndRebuildMode();
                        if (_needsFlush)
                            await Flush();
                        await SaveToken(_lastToken);
                        _lastToken = null;
                    }
                    else if (nextEvent.Event != null)
                    {
                        _logger.EventReceived(
                            _processName, nextEvent.Event.GetType().Name,
                            nextEvent.Token, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();

                        _lastToken = nextEvent.Token;
                        var succeeded = await CallHandler(nextEvent.Event, _lastToken);
                        _logger.EventHandlerFinished(_processName, nextEvent.Event, nextEvent.Token, succeeded);
                        _needsFlush = true;

                        if (_cancelPause.IsCancellationRequested)
                        {
                            await Flush();
                            await SaveToken(_lastToken);
                        }
                    }
                }
                _logger.ProjectionStoppedNormally(_processName);
                SetProcessState(ProcessState.Inactive);
            }
            catch (OperationCanceledException)
            {
                _logger.ProjectionStoppedNormally(_processName);
                SetProcessState(ProcessState.Inactive);
            }
            catch (ProjectorMessages.ConcurrencyException)
            {
                _logger.ProjectionStoppedBecauseOfConcurrencyConflict(_processName);
                SetProcessState(ProcessState.Conflicted);
            }
            catch (ProjectorMessages.HandlerFailedException)
            {
                _logger.ProjectionStoppedBecauseHandlerCrash(_processName);
                SetProcessState(ProcessState.Faulted);
            }
            catch (Exception exception)
            {
                _logger.ProjectionCrashed(_processName, exception);
                SetProcessState(ProcessState.Faulted);
            }
            finally
            {
                _streaming.Close();
                if (_rebuildStopwatch != null)
                {
                    _rebuildStopwatch.Stop();
                }
            }
        }

        private async Task InitializeProjection()
        {
            _stopwatch.Start();

            var token = await TaskUtils.Retry(() => _metadata.GetToken(), _time, _cancelPauseToken);
            _logger.ReceivedToken(_processName, token);
            var savedVersion = await TaskUtils.Retry(() => _metadata.GetVersion(), _time, _cancelPauseToken);
            _logger.ReceivedCurrentVersion(_processName, savedVersion);
            _rebuildMode = _projectionInfo.UpgradeMode(savedVersion);
            _logger.DeterminedRebuildMode(_processName, _rebuildMode, savedVersion, _projectionInfo.GetVersion());

            SetProcessState(ProcessState.Running);

            if (_rebuildMode == EventProjectionUpgradeMode.Upgrade)
            {
                await InitializeUpgrade(savedVersion, token);
            }
            else if (_rebuildMode == EventProjectionUpgradeMode.Rebuild)
            {
                await InitializeRebuild(savedVersion);
            }
            else
            {
                InitializeResume(token, savedVersion);
            }
        }

        private async Task InitializeUpgrade(string savedVersion, EventStoreToken token)
        {
            await TaskUtils.Retry(
                () => _projectionInfo.Handle(new ProjectorMessages.UpgradeFrom(savedVersion)),
                _time, _cancelStopToken);
            _logger.UpgradeHandlerFinished(_processName);

            var newVersion = _projectionInfo.GetVersion();
            await TaskUtils.Retry(() => _metadata.SetVersion(newVersion), _time, _cancelStopToken);
            _logger.SavedVersion(_processName, newVersion);

            _rebuildMode = EventProjectionUpgradeMode.NotNeeded;
            var handledTypes = _subscriptions.GetHandledTypes().ToList();
            _streaming.Setup(token, handledTypes, _metadata.ProcessName);
            _logger.HandledTypesSetup(_processName, handledTypes);

            _logger.UpgradeFinished(_processName, savedVersion, newVersion, token, _stopwatch.ElapsedMilliseconds);
            _stopwatch.Restart();
        }

        private async Task InitializeRebuild(string savedVersion)
        {
            await TaskUtils.Retry(() => _projectionInfo.Handle(new ProjectorMessages.Reset()), _time, _cancelStopToken);
            _logger.ResetHandlerFinished(_processName);

            await TaskUtils.Retry(() => _metadata.SetToken(EventStoreToken.Initial), _time, _cancelStopToken);
            var token = EventStoreToken.Initial;
            _logger.SavedToken(_processName, token);

            var newVersion = _projectionInfo.GetVersion();
            await TaskUtils.Retry(() => _metadata.SetVersion(newVersion), _time, _cancelStopToken);
            _logger.SavedVersion(_processName, newVersion);

            var handledTypes = _subscriptions.GetHandledTypes().ToList();
            _streaming.Setup(token, handledTypes, _metadata.ProcessName);
            _logger.HandledTypesSetup(_processName, handledTypes);

            _logger.RebuildStarted(_processName, savedVersion, newVersion, token, _stopwatch.ElapsedMilliseconds);
            _stopwatch.Restart();

            _rebuildStopwatch = new Stopwatch();
            _rebuildStopwatch.Start();
        }

        private void InitializeResume(EventStoreToken token, string savedVersion)
        {
            var handledTypes = _subscriptions.GetHandledTypes().ToList();
            _streaming.Setup(token, handledTypes, _metadata.ProcessName);
            _logger.HandledTypesSetup(_processName, handledTypes);

            _logger.ResumedNormally(_processName, savedVersion, token, _stopwatch.ElapsedMilliseconds);
            _stopwatch.Restart();
        }

        private async Task<EventStreamingDeserializedEvent> GetNextEvent()
        {
            _cancelPauseToken.ThrowIfCancellationRequested();
            var nowait = _firstIteration || _lastToken != null ||
                         _rebuildMode == EventProjectionUpgradeMode.Rebuild;
            _firstIteration = false;
            var nextEvent = await TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _time, _cancelPauseToken);
            return nextEvent;
        }

        private async Task EndRebuildMode()
        {
            await CallHandler(new ProjectorMessages.RebuildFinished(), null);
            _rebuildMode = EventProjectionUpgradeMode.NotNeeded;
            _needsFlush = true;
            Debug.Assert(_rebuildStopwatch != null, "rebuildStopwatch != null");
            _rebuildStopwatch.Stop();
            _logger.RebuildFinished(_processName, _processedEventsCount, _rebuildStopwatch.ElapsedMilliseconds);
            _rebuildStopwatch = null;
        }

        private async Task Flush()
        {
            await CallHandler(new ProjectorMessages.Flush(), null);
            _needsFlush = false;
            _logger.Flushed(_processName);
        }

        private async Task SaveToken(EventStoreToken tokenToSave)
        {
            if (tokenToSave != null)
            {
                _stopwatch.Restart();
                while (true)
                {
                    try
                    {
                        _cancelStopToken.ThrowIfCancellationRequested();
                        await _metadata.SetToken(tokenToSave);
                        _logger.TokenSaved(_processName, tokenToSave, _stopwatch.ElapsedMilliseconds);
                        return;
                    }
                    catch (TransientErrorException exception)
                    {
                        _logger.TokenSavingFailed(_processName, tokenToSave, exception);
                    }
                }
            }
        }

        private async Task<bool> CallHandler(object message, EventStoreToken token)
        {
            var handler = _subscriptions.FindHandler(message.GetType());
            if (handler == null)
            {
                _logger.NoHandlerFound(_processName, message.GetType());
                return false;
            }
            var handlerSucceeded = await CallHandler(message, token, handler);
            _processedEventsCount++;
            _tracker.ReportProgress(token);
            if (!handlerSucceeded)
                await MarkAsDeadLetter(token);
            return handlerSucceeded;
        }

        private async Task<bool> CallHandler(object message, EventStoreToken token, IEventSubscription handler)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            while (true)
            {
                try
                {
                    _cancelStopToken.ThrowIfCancellationRequested();
                    await handler.Handle(message);
                    _logger.HandlerFinished(_processName, message.GetType(), token, _stopwatch.ElapsedMilliseconds);
                    return true;
                }
                catch (TransientErrorException exception)
                {
                    _logger.TransientErrorWhileCallingHandler(_processName, message.GetType(), token, exception);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.HandlerFailed(_processName, message.GetType(), token, exception);
                    if (_useDeadLetters)
                        return false;
                    else
                        throw new ProjectorMessages.HandlerFailedException(exception);
                }
                finally
                {
                    stopwatch.Stop();
                }
            }
        }

        private async Task MarkAsDeadLetter(EventStoreToken token)
        {
            while (true)
            {
                try
                {
                    _cancelStopToken.ThrowIfCancellationRequested();
                    await _streaming.MarkAsDeadLetter();
                    _logger.MarkedAsDeadLetter(_processName, token);
                    return;
                }
                catch (TransientErrorException exception)
                {
                    _logger.MarkingAsDeadLetterFailed(_processName, token, exception);
                }
            }
        }
    }

    public class EventProjectorSimpleTraceSource : TraceSource
    {
        public EventProjectorSimpleTraceSource(string name)
            : base(name)
        {
        }

        public void ReceivedToken(string processName, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "{ProcessName} starts at token {Token}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void ReceivedCurrentVersion(string processName, string savedVersion)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "{ProcessName} is currently at version {Version}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Version", false, savedVersion);
            msg.Log(this);
        }

        public void DeterminedRebuildMode(string processName, EventProjectionUpgradeMode rebuildMode, string originalVersion, string newVersion)
        {
            string summary;
            if (rebuildMode == EventProjectionUpgradeMode.NotNeeded)
                summary = "{ProcessName} will resume normally";
            else if (rebuildMode == EventProjectionUpgradeMode.Rebuild)
                summary = "{ProcessName} will be rebuilt";
            else
                summary = "{ProcessName} will be upgraded";
            
            var msg = new LogContextMessage(TraceEventType.Information, 3, summary);
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("RebuildMode", false, rebuildMode);
            msg.SetProperty("OriginalVersion", false, originalVersion);
            msg.SetProperty("NewVersion", false, newVersion);
            msg.Log(this);
        }

        public void UpgradeHandlerFinished(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 6, "Upgrade handler for {ProcessName} finished");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void UpgradeFinished(string processName, string originalVersion, string newVersion, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 7, "{ProcessName} was upgraded to {NewVersion}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("OriginalVersion", false, originalVersion);
            msg.SetProperty("NewVersion", false, newVersion);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void ResetHandlerFinished(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 11, "Reset handler for {ProcessName} finished");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void RebuildStarted(string processName, string originalVersion, string newVersion, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 12, "{ProcessName} starts it rebuild at version {NewVersion}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("OriginalVersion", false, originalVersion);
            msg.SetProperty("NewVersion", false, newVersion);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void ResumedNormally(string processName, string version, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 16, "{ProcessName} resumes");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Version", false, version);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }
        
        public void SavedVersion(string processName, string newVersion)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 21, "Saved version {Version} for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Version", false, newVersion);
            msg.Log(this);
        }

        public void SavedToken(string processName, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 23, "Saved token {Token} for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void HandledTypesSetup(string processName, IList<Type> handledTypes)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 25, "{ProcessName} will handle {TypesCount} types");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("TypesCount", false, handledTypes.Count);
            msg.Log(this);
        }

        public void NoEventsAvailable(string processName, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 31, "No new events available for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void EventReceived(string processName, string typeName, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 32, "Got event {Type} for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Type", false, typeName);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void RebuildFinished(string processName, int processedEventsCount, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 14, "Rebuild of {ProcessName} finished");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("EventsCount", false, processedEventsCount);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void Flushed(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 27, "Flushed {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void TokenSaved(string processName, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 23, "Saved token {Token} for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void TokenSavingFailed(string processName, EventStoreToken token, TransientErrorException exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 24, "There is a temporary problem with saving token {Token} for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void NoHandlerFound(string processName, Type type)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 35, 
                "No event handler found for type {Type} at projection {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Type", false, type.Name);
            msg.Log(this);
        }

        public void HandlerFinished(string processName, Type type, EventStoreToken token, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 36,
                "Handler {Type} at projection {ProcessName} finished");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Type", false, type.Name);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void TransientErrorWhileCallingHandler(string processName, Type type, EventStoreToken token, TransientErrorException exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 37,
                "Handler {Type} at projection {ProcessName} encountered temporary problem");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Type", false, type.Name);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void HandlerFailed(string processName, Type type, EventStoreToken token, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 38,
                "Handler {Type} at projection {ProcessName} failed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Type", false, type.Name);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void MarkedAsDeadLetter(string processName, EventStoreToken token)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 40, 
                "Event {Token} marked as dead-letter in {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.Log(this);
        }

        public void MarkingAsDeadLetterFailed(string processName, EventStoreToken token, TransientErrorException exception)
        {
            var msg = new LogContextMessage(
                   TraceEventType.Verbose, 41,
                   "There is a temporay problem with marking event {Token} as dead-letter in {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void EventHandlerFinished(string processName, object eventData, EventStoreToken token, bool succeeded)
        {
            var msg = new LogContextMessage(
                TraceEventType.Verbose, 34, 
                "Event {Token} of type {Type} processed in {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Type", false, eventData.GetType().Name);
            msg.SetProperty("Token", false, token);
            msg.SetProperty("Succeeded", false, succeeded);
            msg.Log(this);
        }

        public void ProjectionStoppedNormally(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 91, "{ProcessName} stopped normally");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void ProjectionStoppedBecauseOfConcurrencyConflict(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 92, "{ProcessName} stopped because of concurrency conflict");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void ProjectionStoppedBecauseHandlerCrash(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 93, "{ProcessName} stopped because a handler crashed");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void ProjectionCrashed(string processName, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 94, "{ProcessName} crashed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void SetProcessStateHandlerFailed(string processName, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 98, "Handler of SetProcessState crashed");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }
}