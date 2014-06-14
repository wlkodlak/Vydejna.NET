using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventProjector
    {
        void Register<T>(IProcess<T> handler);
    }
    public interface IEventProjection
        : IProcess<ProjectorMessages.Reset>
        , IProcess<ProjectorMessages.UpgradeFrom>
    {
        string GetVersion();
        EventProjectionUpgradeMode UpgradeMode(string storedVersion);
    }
    public enum EventProjectionUpgradeMode { NotNeeded, Rebuild, Upgrade }
    public static class ProjectorMessages
    {
        public class ConcurrencyException : Exception { }
        public class RebuildFinished { }
        public class UpgradeFrom
        {
            private readonly string _version;
            public string Version { get { return _version; } }
            public UpgradeFrom(string version) { _version = version; }
        }
        public class Reset { }
        public class Flush { }
        public class Resume { }
    }

    public class EventProjectorSimple : IEventProjector, IProcessWorker
    {
        private readonly object _lock;
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private readonly IEventProjection _projectionInfo;
        private readonly ITime _time;
        private readonly string _logName;
        private readonly Stopwatch _stopwatch;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.EventProjectorSimple");
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private CancellationTokenSource _cancelPause, _cancelStop;
        private bool _useDeadLetters;
        private CancellationToken _cancelPauseToken;
        private CancellationToken _cancelStopToken;
        private TaskScheduler _scheduler;
        private int _processedEventsCount;
        private bool _exceptionAlreadyLogged;

        public EventProjectorSimple(IEventProjection projection, IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions, ITime time)
        {
            _lock = new object();
            _projectionInfo = projection;
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _time = time;
            _logName = _metadata.ProcessName;
            _stopwatch = new Stopwatch();
        }

        public void Register<T>(IProcess<T> handler)
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
                if (_processState == ProcessState.Inactive && (state == ProcessState.Pausing || state == ProcessState.Stopping))
                    return;
                _processState = state;
                handler = _onStateChanged;
            }
            if (handler != null)
            {
                try { handler(state); }
                catch { }
            }
        }

        public void Start()
        {
            _cancelPause = new CancellationTokenSource();
            _cancelPauseToken = _cancelPause.Token;
            _cancelStop = new CancellationTokenSource();
            _cancelStopToken = _cancelStop.Token;
            SetProcessState(ProcessState.Starting);
            TaskUtils.FromEnumerable(ProjectorCore()).UseScheduler(_scheduler).GetTask().ContinueWith(Finish, _scheduler);
        }

        private void Finish(Task task)
        {
            ProcessState newState;
            if (task.Exception == null || task.IsCanceled)
                newState = ProcessState.Inactive;
            else if (task.Exception.InnerException is ProjectorMessages.ConcurrencyException)
                newState = ProcessState.Conflicted;
            else
            {
                newState = ProcessState.Faulted;
                if (!_exceptionAlreadyLogged)
                    Logger.ErrorFormat("{0}: Processing failed. {1}", _logName, task.Exception.InnerException);
            }
            SetProcessState(newState);
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
            _processState = ProcessState.Uninitialized;
        }

        private IEnumerable<Task> ProjectorCore()
        {
            Stopwatch rebuildStopwatch = null;
            try
            {
                _stopwatch.Start();

                var taskGetToken = TaskUtils.Retry(() => _metadata.GetToken(), _time, _cancelPauseToken);
                yield return taskGetToken;
                var token = taskGetToken.Result;
                Logger.TraceFormat("{0}: Loaded token {1}.", _logName, token);

                var taskGetVersion = TaskUtils.Retry(() => _metadata.GetVersion(), _time, _cancelPauseToken);
                yield return taskGetVersion;
                var savedVersion = taskGetVersion.Result;
                Logger.TraceFormat("{0}: Loaded version {1}.", _logName, savedVersion);

                var rebuildMode = _projectionInfo.UpgradeMode(savedVersion);
                Logger.TraceFormat("{0}: Upgrade mode {1}.", _logName, rebuildMode);

                SetProcessState(ProcessState.Running);

                var firstIteration = true;
                var lastToken = (EventStoreToken)null;
                var handledTypes = _subscriptions.GetHandledTypes();
                Logger.TraceFormat("{0}: Projection will handle types: {1}", _logName, string.Join(", ", handledTypes.Select(t => t.Name)));

                if (rebuildMode == EventProjectionUpgradeMode.Upgrade)
                {
                    var taskUpgrade = TaskUtils.Retry(() => _projectionInfo.Handle(new ProjectorMessages.UpgradeFrom(savedVersion)), _time, _cancelStopToken);
                    yield return taskUpgrade;
                    taskUpgrade.Wait();
                    Logger.TraceFormat("{0}: Upgrade handler finished.", _logName);

                    var newVersion = _projectionInfo.GetVersion();
                    var taskSetVersion = TaskUtils.Retry(() => _metadata.SetVersion(newVersion), _time, _cancelStopToken);
                    yield return taskSetVersion;
                    taskSetVersion.Wait();
                    Logger.TraceFormat("{0}: Saved version {1}.", _logName, newVersion);

                    rebuildMode = EventProjectionUpgradeMode.NotNeeded;
                    _streaming.Setup(token, handledTypes, _metadata.ProcessName);

                    Logger.InfoFormat("{0}: Upgraded from version {1} to version {2}. Process will continue from token {3}. Initialization took {4} ms.", 
                        _logName, savedVersion, newVersion, token, _stopwatch.ElapsedMilliseconds);
                    _stopwatch.Restart();
                }
                else if (rebuildMode == EventProjectionUpgradeMode.Rebuild)
                {
                    var taskReset = TaskUtils.Retry(() => _projectionInfo.Handle(new ProjectorMessages.Reset()), _time, _cancelStopToken);
                    yield return taskReset;
                    taskReset.Wait();
                    Logger.TraceFormat("{0}: Reset handler finished.", _logName);

                    var taskSetToken = TaskUtils.Retry(() => _metadata.SetToken(EventStoreToken.Initial), _time, _cancelStopToken);
                    yield return taskSetToken;
                    taskSetToken.Wait();
                    token = EventStoreToken.Initial;
                    Logger.TraceFormat("{0}: Saved initial token.", _logName);

                    var newVersion = _projectionInfo.GetVersion();
                    var taskSetVersion = TaskUtils.Retry(() => _metadata.SetVersion(newVersion), _time, _cancelStopToken);
                    yield return taskSetVersion;
                    taskSetVersion.Wait();
                    Logger.TraceFormat("{0}: Saved version {1}.", _logName, newVersion);

                    _streaming.Setup(token, handledTypes, _metadata.ProcessName);

                    Logger.InfoFormat("{0}: Starting rebuild at version {1} (original version {2}). Initialization took {3} ms.",
                        _logName, newVersion, savedVersion ?? "none", _stopwatch.ElapsedMilliseconds);
                    _stopwatch.Restart();

                    rebuildStopwatch = new Stopwatch();
                    rebuildStopwatch.Start();
                }
                else
                {
                    _streaming.Setup(token, handledTypes, _metadata.ProcessName);

                    Logger.InfoFormat("{0}: Resuming normal processing at version {1}. Processing will continue from token {2}. Initialization took {3} ms.",
                        _logName, savedVersion, token, _stopwatch.ElapsedMilliseconds);
                    _stopwatch.Restart();
                }
                var needsFlush = false;

                while (!_cancelPauseToken.IsCancellationRequested)
                {
                    _stopwatch.Restart();
                    var nowait = firstIteration || lastToken != null || rebuildMode == EventProjectionUpgradeMode.Rebuild;
                    firstIteration = false;
                    var taskNextEvent = TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _time, _cancelPauseToken);
                    yield return taskNextEvent;
                    var nextEvent = taskNextEvent.Result;

                    var tokenToSave = (EventStoreToken)null;
                    if (nextEvent == null)
                    {
                        Logger.TraceFormat("{0}: No events are available (attempt took {1} ms).", _logName, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();
                        if (rebuildMode == EventProjectionUpgradeMode.Rebuild)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.RebuildFinished(), null);
                            yield return taskHandler;
                            taskHandler.Wait();
                            rebuildMode = EventProjectionUpgradeMode.NotNeeded;
                            needsFlush = true;
                            rebuildStopwatch.Stop();
                            Logger.InfoFormat("{0}: Rebuild finished ({1} events in {2} ms).", 
                                _logName, _processedEventsCount, rebuildStopwatch.ElapsedMilliseconds);
                            rebuildStopwatch = null;
                        }
                        if (needsFlush)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.Flush(), null);
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = false;
                            Logger.TraceFormat("{0}: Flushed.", _logName);
                        }
                        tokenToSave = lastToken;
                        lastToken = null;
                    }
                    else if (nextEvent.Event != null)
                    {
                        Logger.TraceFormat("{0}: Received event {1} (token {2}) in {3} ms.", 
                            _logName, nextEvent.Event.GetType().Name, nextEvent.Token, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();

                        lastToken = nextEvent.Token;
                        {
                            var taskHandler = CallHandler(nextEvent.Event, lastToken);
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = true;
                        }

                        if (_cancelPause.IsCancellationRequested)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.Flush(), null);
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = false;
                            tokenToSave = lastToken;
                            Logger.TraceFormat("{0}: Flushed (because of cancellation).", _logName);
                        }
                    }

                    if (tokenToSave != null)
                    {
                        _stopwatch.Restart();
                        while (!_cancelStopToken.IsCancellationRequested)
                        {
                            var taskSaveToken = _metadata.SetToken(tokenToSave);
                            yield return taskSaveToken;
                            if (taskSaveToken.Exception == null)
                            {
                                Logger.TraceFormat("{0}: Saved token {1} in {2} ms", _logName, tokenToSave, _stopwatch.ElapsedMilliseconds);
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                _streaming.Close();
                if (rebuildStopwatch != null)
                {
                    rebuildStopwatch.Stop();
                }
            }
        }

        private Task CallHandler(object message, EventStoreToken token)
        {
            return new CallHandlerContext(this, message, token).Execute();
        }

        private class CallHandlerContext
        {
            private readonly EventProjectorSimple _parent;
            private readonly object _message;
            private readonly Stopwatch _stopwatch;
            private readonly EventStoreToken _token;
            private ICommandSubscription _handler;
            private string _typeName;

            public CallHandlerContext(EventProjectorSimple parent, object message, EventStoreToken token)
            {
                _parent = parent;
                _message = message;
                _token = token;
                _stopwatch = new Stopwatch();
            }

            public Task Execute()
            {
                var type = _message.GetType();
                _typeName = type.Name;
                _handler = _parent._subscriptions.FindHandler(type);
                if (_handler == null)
                {
                    Logger.TraceFormat("{0}: Handler for {1} not found, skipping.", _parent._logName, _typeName);
                    return TaskUtils.CompletedTask();
                }
                else
                {
                    _stopwatch.Start();
                    return CallHandler();
                }
            }

            private Task CallHandler()
            {
                if (_parent._cancelStopToken.IsCancellationRequested)
                    return TaskUtils.CancelledTask<object>();
                else
                    return _handler.Handle(_message).ContinueWith<Task>(HandlerFinished).Unwrap();
            }

            private Task HandlerFinished(Task task)
            {
                if (task.IsCanceled)
                {
                    _stopwatch.Stop();
                    return task;
                }
                else if (task.Exception == null)
                {
                    _stopwatch.Stop();
                    _parent._processedEventsCount++;
                    Logger.InfoFormat("{0}: Event {1} (token {2}) processed in {3} ms.", 
                        _parent._logName, _typeName, _token, _stopwatch.ElapsedMilliseconds);
                    return task;
                }
                else if (task.Exception.InnerException is TransientErrorException)
                {
                    Logger.TraceFormat("{0}: Transient error occurred, retrying.", _parent._logName);
                    return CallHandler();
                }
                else if (!_parent._useDeadLetters)
                {
                    _stopwatch.Stop();
                    Logger.ErrorFormat("{0}: Error when processing event {1} (token {2}). {3}",
                        _parent._logName, _typeName, _token, task.Exception.InnerException);
                    _parent._exceptionAlreadyLogged = true;
                    return task;
                }
                else
                {
                    _stopwatch.Stop();
                    _parent._processedEventsCount++;
                    Logger.ErrorFormat("{0}: Error when processing event {1} (token {2}), putting to dead-letter. {3}",
                        _parent._logName, _typeName, _token, task.Exception.InnerException);
                    return MarkAsDeadLetter();
                }
            }

            private Task MarkAsDeadLetter()
            {
                if (_parent._cancelStopToken.IsCancellationRequested)
                    return TaskUtils.CancelledTask<object>();
                else
                    return _parent._streaming.MarkAsDeadLetter().ContinueWith<Task>(DeadLetterFinished).Unwrap();
            }

            private Task DeadLetterFinished(Task task)
            {
                if (task.IsCanceled || task.Exception == null)
                    return task;
                else if (task.Exception.InnerException is TransientErrorException)
                    return MarkAsDeadLetter();
                else
                    return task;
            }
        }
    }
}
