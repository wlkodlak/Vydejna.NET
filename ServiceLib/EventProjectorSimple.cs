using System;
using System.Collections.Generic;
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
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private CancellationTokenSource _cancelPause, _cancelStop;
        private bool _useDeadLetters;
        private CancellationToken _cancelPauseToken;
        private CancellationToken _cancelStopToken;
        private TaskScheduler _scheduler;

        public EventProjectorSimple(IEventProjection projection, IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _lock = new object();
            _projectionInfo = projection;
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _cancelPause = new CancellationTokenSource();
            _cancelPauseToken = _cancelPause.Token;
            _cancelStop = new CancellationTokenSource();
            _cancelStopToken = _cancelStop.Token;
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
            SetProcessState(ProcessState.Starting);
            TaskUtils.FromEnumerable(ProjectorCore()).UseScheduler(_scheduler).GetTask().ContinueWith(Finish, _scheduler);
        }

        private void Finish(Task task)
        {
            SetProcessState((task.Exception == null || task.IsCanceled) ? ProcessState.Inactive : ProcessState.Faulted);
        }

        public void Pause()
        {
            SetProcessState(ProcessState.Pausing);
            _cancelPause.Cancel();
            _streaming.Dispose();
        }

        public void Stop()
        {
            SetProcessState(ProcessState.Stopping);
            _cancelPause.Cancel();
            _cancelStop.Cancel();
            _streaming.Dispose();
        }

        public void Dispose()
        {
            _cancelPause.Cancel();
            _cancelStop.Cancel();
            _cancelPause.Dispose();
            _cancelStop.Dispose();
            _processState = ProcessState.Uninitialized;
        }

        private IEnumerable<Task> ProjectorCore()
        {
            yield return TaskUtils.Retry(() => _metadata.Lock(_cancelPauseToken));
            try
            {
                var taskGetToken = TaskUtils.Retry(() => _metadata.GetToken(), _cancelPauseToken);
                yield return taskGetToken;
                var token = taskGetToken.Result;

                var taskGetVersion = TaskUtils.Retry(() => _metadata.GetVersion(), _cancelPauseToken);
                yield return taskGetVersion;
                var savedVersion = taskGetVersion.Result;

                var rebuildMode = _projectionInfo.UpgradeMode(savedVersion);

                SetProcessState(ProcessState.Running);

                var firstIteration = true;
                var lastToken = (EventStoreToken)null;

                if (rebuildMode == EventProjectionUpgradeMode.Upgrade)
                {
                    var taskUpgrade = TaskUtils.Retry(() => _projectionInfo.Handle(new ProjectorMessages.UpgradeFrom(savedVersion)), _cancelStopToken);
                    yield return taskUpgrade;
                    taskUpgrade.Wait();

                    var taskSetVersion = TaskUtils.Retry(() => _metadata.SetVersion(_projectionInfo.GetVersion()), _cancelStopToken);
                    yield return taskSetVersion;
                    taskSetVersion.Wait();

                    rebuildMode = EventProjectionUpgradeMode.NotNeeded;
                }
                else if (rebuildMode == EventProjectionUpgradeMode.Rebuild)
                {
                    var taskReset = TaskUtils.Retry(() => _projectionInfo.Handle(new ProjectorMessages.Reset()), _cancelStopToken);
                    yield return taskReset;
                    taskReset.Wait();

                    var taskSetToken = TaskUtils.Retry(() => _metadata.SetToken(EventStoreToken.Initial), _cancelStopToken);
                    yield return taskSetToken;
                    taskSetToken.Wait();
                    token = EventStoreToken.Initial;

                    var taskSetVersion = TaskUtils.Retry(() => _metadata.SetVersion(_projectionInfo.GetVersion()), _cancelStopToken);
                    yield return taskSetVersion;
                    taskSetVersion.Wait();
                }
                _streaming.Setup(token, _subscriptions.GetHandledTypes(), _metadata.ProcessName);
                var needsFlush = false;

                while (!_cancelPauseToken.IsCancellationRequested)
                {
                    var nowait = firstIteration || lastToken != null || rebuildMode == EventProjectionUpgradeMode.Rebuild;
                    firstIteration = false;
                    var taskNextEvent = TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _cancelPauseToken);
                    yield return taskNextEvent;
                    var nextEvent = taskNextEvent.Result;

                    var tokenToSave = (EventStoreToken)null;
                    if (nextEvent == null)
                    {
                        if (rebuildMode == EventProjectionUpgradeMode.Rebuild)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.RebuildFinished());
                            yield return taskHandler;
                            taskHandler.Wait();
                            rebuildMode = EventProjectionUpgradeMode.NotNeeded;
                            needsFlush = true;
                        }
                        if (needsFlush)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.Flush());
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = false;
                        }
                        tokenToSave = lastToken;
                        lastToken = null;
                    }
                    else if (nextEvent.Event != null)
                    {
                        lastToken = nextEvent.Token;
                        {
                            var taskHandler = CallHandler(nextEvent.Event);
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = true;
                        }

                        if (_cancelPause.IsCancellationRequested)
                        {
                            var taskHandler = CallHandler(new ProjectorMessages.Flush());
                            yield return taskHandler;
                            taskHandler.Wait();
                            needsFlush = false;
                            tokenToSave = lastToken;
                        }
                    }

                    if (tokenToSave != null)
                    {
                        while (!_cancelStopToken.IsCancellationRequested)
                        {
                            var taskSaveToken = _metadata.SetToken(tokenToSave);
                            yield return taskSaveToken;
                            if (taskSaveToken.Exception == null)
                                break;
                        }
                    }

                }
            }
            finally
            {
                _metadata.Unlock();
                _streaming.Dispose();
            }
        }

        private Task CallHandler(object message)
        {
            return new CallHandlerContext(this, message).Execute();
        }

        private class CallHandlerContext
        {
            private EventProjectorSimple _parent;
            private object _message;
            private ICommandSubscription _handler;

            public CallHandlerContext(EventProjectorSimple parent, object message)
            {
                _parent = parent;
                _message = message;
            }

            public Task Execute()
            {
                _handler = _parent._subscriptions.FindHandler(_message.GetType());
                if (_handler == null)
                    return TaskUtils.CompletedTask();
                else
                    return CallHandler();
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
                if (task.IsCanceled || task.Exception == null)
                    return task;
                else if (task.Exception.InnerException is TransientErrorException)
                    return CallHandler();
                else if (!_parent._useDeadLetters)
                    return task;
                else
                    return MarkAsDeadLetter();
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
