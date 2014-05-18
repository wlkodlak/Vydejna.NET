﻿using System;
using System.Collections.Generic;
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
        private CancellationTokenSource _cancelPause, _cancelStop;
        private ProcessState _processState;
        private Action<ProcessState> _onProcessStateChanged;
        private int _flushAfter;
        private TaskScheduler _scheduler;

        public EventProcessSimple(IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _processState = ProcessState.Uninitialized;
            _lock = new object();
            _cancelPause = new CancellationTokenSource();
            _cancelStop = new CancellationTokenSource();
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
            SetProcessState(ProcessState.Starting);
            TaskUtils.FromEnumerable(ProcessCore()).CatchAll().UseScheduler(_scheduler).GetTask()
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
            yield return TaskUtils.Retry(() => _metadata.Lock(_cancelPause.Token));
            try
            {
                var taskGetToken = TaskUtils.Retry(() => _metadata.GetToken(), _cancelPause.Token);
                yield return taskGetToken;
                var token = taskGetToken.Result;

                SetProcessState(ProcessState.Running);
                _streaming.Setup(token, _subscriptions.GetHandledTypes(), _metadata.ProcessName);

                var firstIteration = true;
                var lastToken = (EventStoreToken)null;

                while (!_cancelPause.IsCancellationRequested)
                {
                    var nowait = firstIteration || lastToken != null;
                    firstIteration = false;
                    var taskNextEvent = TaskUtils.Retry(() => _streaming.GetNextEvent(nowait), _cancelPause.Token);
                    yield return taskNextEvent;
                    if (_cancelPause.IsCancellationRequested)
                        break;
                    var nextEvent = taskNextEvent.Result;

                    var tokenToSave = (EventStoreToken)null;
                    if (nextEvent == null)
                    {
                        tokenToSave = lastToken;
                        lastToken = null;
                    }
                    else if (nextEvent.Event != null)
                    {
                        lastToken = nextEvent.Token;
                        var handler = _subscriptions.FindHandler(nextEvent.Event.GetType());
                        var taskHandler = TaskUtils.Retry(() => handler.Handle(nextEvent.Event), _cancelStop.Token, 3);
                        yield return taskHandler;
                        tokenToSave = lastToken;
                        if (taskHandler.Exception != null && !taskHandler.IsCanceled)
                        {
                            while (true)
                            {
                                _cancelStop.Token.ThrowIfCancellationRequested();
                                var taskDead = _streaming.MarkAsDeadLetter();
                                yield return taskDead;
                                if (taskDead.Exception == null)
                                    break;
                            }
                        }
                    }

                    if (tokenToSave != null)
                    {
                        while (!_cancelStop.IsCancellationRequested)
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
            }
        }
    }

    public static class ProjectorUtils
    {
        public static Task<int> CheckConcurrency(Task<bool> saved, int savedAsVersion)
        {
            var tcs = new TaskCompletionSource<int>();
            if (saved.IsCanceled)
                tcs.SetCanceled();
            else if (saved.Exception != null)
                tcs.SetException(saved.Exception.InnerExceptions);
            else if (saved.Result)
                tcs.SetResult(savedAsVersion);
            else
                tcs.SetException(new ProjectorMessages.ConcurrencyException());
            return tcs.Task;
        }

        public static Task<MemoryCacheItem<T>> LoadFromDocument<T>(Task<DocumentStoreFoundDocument> task, Func<string, T> deserializer)
        {
            var tcs = new TaskCompletionSource<MemoryCacheItem<T>>();
            if (task.IsCanceled)
                tcs.SetCanceled();
            else if (task.Exception != null)
                tcs.SetException(task.Exception.InnerExceptions);
            else
            {
                var result = task.Result;
                if (result == null)
                    tcs.SetResult(new MemoryCacheItem<T>(0, default(T)));
                else if (!result.HasNewerContent)
                    tcs.SetResult(null);
                else if (string.IsNullOrEmpty(result.Contents))
                {
                    tcs.SetResult(new MemoryCacheItem<T>(result.Version, default(T)));
                }
                else
                {
                    try
                    {
                        tcs.SetResult(new MemoryCacheItem<T>(result.Version, deserializer(result.Contents)));
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }
            }
            return tcs.Task;
        }
    }
}
