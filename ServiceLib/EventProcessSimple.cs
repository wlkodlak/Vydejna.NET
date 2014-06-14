using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class EventProcessSimple
        : IProcessWorker
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private readonly object _lock;
        private readonly Stopwatch _stopwatch;
        private readonly string _logName;
        private CancellationTokenSource _cancelPause, _cancelStop;
        private ProcessState _processState;
        private Action<ProcessState> _onProcessStateChanged;
        private int _flushAfter;
        private TaskScheduler _scheduler;
        private ITime _time;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.EventProcessSimple");

        public EventProcessSimple(
            IMetadataInstance metadata, IEventStreamingDeserialized streaming,
            ICommandSubscriptionManager subscriptions, ITime time)
        {
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _processState = ProcessState.Uninitialized;
            _time = time;
            _logName = _metadata.ProcessName;
            _stopwatch = new Stopwatch();
            _lock = new object();
        }

        public EventProcessSimple Register<T>(IProcess<T> handler)
        {
            _subscriptions.Register(handler);
            return this;
        }

        public EventProcessSimple WithTokenFlushing(int flushAfter)
        {
            _flushAfter = flushAfter;
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
            _cancelStop = new CancellationTokenSource();
            SetProcessState(ProcessState.Starting);
            TaskUtils.FromEnumerable(ProcessCore()).UseScheduler(_scheduler).GetTask()
                .ContinueWith(t =>
                {
                    if (t.Exception == null || t.IsCanceled)
                        SetProcessState(ProcessState.Inactive);
                    else
                        SetProcessState(ProcessState.Faulted);
                }, _scheduler);
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
            SetProcessState(ProcessState.Inactive);
        }

        private void SetProcessState(ProcessState newState)
        {
            Action<ProcessState> handler;
            lock (_lock)
            {
                if (_processState == newState)
                    return;
                if (_processState == ProcessState.Inactive && (newState == ProcessState.Pausing || newState == ProcessState.Stopping))
                    return;
                _processState = newState;
                handler = _onProcessStateChanged;
            }
            try
            {
                if (handler != null)
                    handler(newState);
            }
            catch
            {
            }
        }

        private IEnumerable<Task> ProcessCore()
        {
            try
            {
                _stopwatch.Start();
                var taskGetToken = TaskUtils.Retry(() => _metadata.GetToken(), _time, _cancelPause.Token);
                yield return taskGetToken;
                var token = taskGetToken.Result;
                Logger.DebugFormat("{0}: Starting processing at token {1}", _logName, token);

                SetProcessState(ProcessState.Running);
                var handledTypes = _subscriptions.GetHandledTypes();
                _streaming.Setup(token, handledTypes, _metadata.ProcessName);
                if (Logger.IsDebugEnabled)
                {
                    Logger.TraceFormat("{0}: Process will use these event types: {1}",
                        _logName, string.Join(", ", handledTypes.Select(t => t.Name)));
                }

                Logger.TraceFormat("{0}: Initialization took {1} ms", _logName, _stopwatch.ElapsedMilliseconds);
                _stopwatch.Restart();

                var firstIteration = true;
                var lastToken = (EventStoreToken)null;

                while (!_cancelPause.IsCancellationRequested)
                {
                    var nowait = firstIteration || lastToken != null;
                    firstIteration = false;
                    var taskNextEvent = TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _time, _cancelPause.Token);
                    yield return taskNextEvent;
                    if (_cancelPause.IsCancellationRequested)
                        break;
                    var nextEvent = taskNextEvent.Result;

                    var tokenToSave = (EventStoreToken)null;
                    if (nextEvent == null)
                    {
                        tokenToSave = lastToken;
                        lastToken = null;
                        Logger.TraceFormat("{0}: No events to process (attempt to get an event took {1} ms)",
                            _logName, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();
                    }
                    else if (nextEvent.Event != null)
                    {
                        lastToken = nextEvent.Token;
                        var eventType = nextEvent.Event.GetType();
                        var handler = _subscriptions.FindHandler(eventType);
                        Logger.TraceFormat("{0}: Received event of type {1}, token {2} (getting the event took {3} ms)",
                            _logName, eventType.Name, nextEvent.Token, _stopwatch.ElapsedMilliseconds);
                        _stopwatch.Restart();

                        var taskHandler = TaskUtils.Retry(() => handler.Handle(nextEvent.Event), _time, _cancelStop.Token, 3);
                        yield return taskHandler;
                        tokenToSave = lastToken;
                        if (taskHandler.Exception != null && !taskHandler.IsCanceled)
                        {
                            while (true)
                            {
                                _cancelStop.Token.ThrowIfCancellationRequested();
                                var processingTime = _stopwatch.ElapsedMilliseconds;
                                _stopwatch.Restart();
                                var taskDead = _streaming.MarkAsDeadLetter();
                                yield return taskDead;
                                if (taskDead.Exception == null)
                                {
                                    Logger.ErrorFormat("{0}: Event {1} (token {2}) failed in {3} ms, marked as dead-letter in {4} ms. {5}",
                                        _logName, eventType.Name, nextEvent.Token, processingTime, _stopwatch.ElapsedMilliseconds, taskHandler.Exception.InnerException);
                                    _stopwatch.Restart();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Logger.InfoFormat("{0}: Event {1} (token {2}) processed in {3} ms",
                               _logName, eventType.Name, nextEvent.Token, _stopwatch.ElapsedMilliseconds);
                            _stopwatch.Restart();
                        }
                    }

                    if (tokenToSave != null)
                    {
                        while (!_cancelStop.IsCancellationRequested)
                        {
                            var taskSaveToken = _metadata.SetToken(tokenToSave);
                            yield return taskSaveToken;
                            if (taskSaveToken.Exception == null)
                            {
                                Logger.TraceFormat("{0}: Saved token {1} ({2} ms)", _logName, tokenToSave, _stopwatch.ElapsedMilliseconds);
                                _stopwatch.Restart();
                                break;
                            }
                        }
                    }

                }
            }
            finally
            {
                _stopwatch.Stop();
                _streaming.Close();
                Logger.DebugFormat("Processing ended");
            }
        }
    }

    public static class ProjectorUtils
    {
        public static Task<int> Save(IDocumentFolder folder, string documentName, int expectedVersion, string newContents, IList<DocumentIndexing> indexes)
        {
            return folder.SaveDocument(documentName, newContents, DocumentStoreVersion.At(expectedVersion), indexes).ContinueWith(task =>
                {
                    if (task.Result)
                        return expectedVersion + 1;
                    else
                        throw new ProjectorMessages.ConcurrencyException();
                });
        }
    }
}
