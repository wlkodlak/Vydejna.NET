using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ServiceLib
{
    public class EventProcessSimple
        : IProcessWorker
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private EventStoreToken _token;
        private object _handledEvent;
        private int _flushCounter;
        private bool _metadataDirty;
        private IDisposable _waitForLock;
        private int _flushAfter;
        private ProcessState _processState;
        private Action<ProcessState> _onProcessStateChanged;

        public EventProcessSimple(IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _flushAfter = _flushCounter = 20;
            _processState = ProcessState.Uninitialized;
        }

        public IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler)
        {
            return _subscriptions.Register(handler);
        }

        private void ObtainedLock()
        {
            if (_processState == ProcessState.Starting)
                _metadata.GetToken(OnMetadataLoaded, OnError);
            else
            {
                StopRunning(ProcessState.Inactive);
            }
        }

        private void OnMetadataLoaded(EventStoreToken token)
        {
            if (_processState == ProcessState.Starting)
            {
                try
                {
                    SetProcessState(ProcessState.Running);
                    _token = token;
                    _streaming.Setup(_token, _subscriptions.GetHandledTypes().ToArray(), _metadata.ProcessName);
                    _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, false);
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
            else
            {
                StopRunning(ProcessState.Inactive);
            }
        }

        private void EventReceived(EventStoreToken token, object evnt)
        {
            try
            {
                if (_processState == ProcessState.Running)
                {
                    var handler = _subscriptions.FindHandler(evnt.GetType());
                    if (handler == null)
                        EventHandled();
                    else
                    {
                        _token = token;
                        _handledEvent = evnt;
                        handler.Handle(_handledEvent, EventHandled, OnHandlerError);
                    }
                }
                else if (_processState == ProcessState.Pausing)
                    SaveToken();
                else
                    StopRunning(ProcessState.Inactive);
            }
            catch (Exception exception)
            {
                OnError(exception);
            }
        }

        private void NoNewEvents()
        {
            if (_processState == ProcessState.Running || _processState == ProcessState.Pausing)
                SaveToken();
            else
                StopRunning(ProcessState.Inactive);
        }

        private void CannotReceiveEvents(Exception exception, EventStoreEvent evnt)
        {
            OnError(exception);
        }

        private void OnHandlerError(Exception exception)
        {
            _streaming.MarkAsDeadLetter(EventHandled, OnError);
        }

        private void EventHandled()
        {
            _metadataDirty = true;
            _flushCounter--;
            if (_flushCounter > 0)
            {
                _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, true);
            }
            else
                SaveToken();
        }

        private void SaveToken()
        {
            if (_metadataDirty)
            {
                _flushCounter = _flushAfter;
                _metadata.SetToken(_token, OnTokenSaved, OnError);
            }
            else
                OnTokenSaved();
        }

        private void OnTokenSaved()
        {
            if (_processState == ProcessState.Running)
                _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, false);
            else
            {
                StopRunning(ProcessState.Inactive);
            }
        }

        private void OnError(Exception exception)
        {
            StopRunning(ProcessState.Faulted);
        }

        private void StopRunning(ProcessState newState)
        {
            _streaming.Dispose();
            _metadata.Unlock();
            if (_processState != ProcessState.Faulted)
                SetProcessState(newState);
        }

        public EventProcessSimple WithTokenFlushing(int flushAfter)
        {
            _flushCounter = _flushAfter = flushAfter;
            return this;
        }

        public ProcessState State
        {
            get { return _processState; }
        }

        public void Init(Action<ProcessState> onStateChanged)
        {
            _processState = ProcessState.Inactive;
            _onProcessStateChanged = onStateChanged;
        }

        public void Start()
        {
            SetProcessState(ProcessState.Starting);
            _waitForLock = _metadata.Lock(ObtainedLock);
        }

        private void SetProcessState(ProcessState newState)
        {
            _processState = newState;
            if (_onProcessStateChanged != null)
                _onProcessStateChanged(newState);
        }

        public void Pause()
        {
            Stop(false);
        }

        public void Stop()
        {
            Stop(true);
        }

        private void Stop(bool immediatelly)
        {
            if (_processState == ProcessState.Running)
            {
                if (immediatelly)
                    SetProcessState(ProcessState.Stopping);
                else
                    SetProcessState(ProcessState.Pausing);
                _streaming.Dispose();
                _waitForLock.Dispose();
            }
            else
            {
                SetProcessState(ProcessState.Inactive);
                _streaming.Dispose();
                _waitForLock.Dispose();
            }
        }

        public void Dispose()
        {
            _onProcessStateChanged = null;
            _processState = ProcessState.Uninitialized;
        }
    }
}
