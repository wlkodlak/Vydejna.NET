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
        private int _flushAfter;
        private ProcessState _processState;
        private Action<ProcessState> _onProcessStateChanged;
        private int _fsmState;
        private EventStoreToken _token, _lastToken, _tokenToSave;
        private int _flushCounter, _handlerRetriesLeft;
        private object _handledEvent;
        private ICommandSubscription _handler;
        private IDisposable _lockWait;

        public EventProcessSimple(IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
            _flushAfter = 20;
            _processState = ProcessState.Uninitialized;
            _fsmState = 0;
        }

        public IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler)
        {
            return _subscriptions.Register(handler);
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
            _fsmState = 10;
            SetProcessState(ProcessState.Starting);
            ContinueFsm();
        }

        private void SetProcessState(ProcessState newState)
        {
            _processState = newState;
            var handler = _onProcessStateChanged;
            try
            {
                if (handler != null)
                    handler(newState);
            }
            catch
            {
            }
        }

        public void Pause()
        {
            SetProcessState(ProcessState.Pausing);
            if (_lockWait != null)
                _lockWait.Dispose();
            if (_streaming != null)
                _streaming.Dispose();
        }

        public void Stop()
        {
            SetProcessState(ProcessState.Stopping);
            if (_lockWait != null)
                _lockWait.Dispose();
            if (_streaming != null)
                _streaming.Dispose();
        }

        private void ContinueFsm()
        {
            try
            {
                switch (_fsmState)
                {
                    case 10:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 210;
                        else
                        {
                            _lockWait = _metadata.Lock(
                                () => { _fsmState = 20; _lockWait = null; ContinueFsm(); },
                                ex => { _fsmState = ex is TransientErrorException ? 10 : 210; _lockWait = null; ContinueFsm(); }
                                );
                            return;
                        }

                    case 20:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 200;
                        else
                        {
                            _metadata.GetToken(
                                t => { _fsmState = 30; _token = t; ContinueFsm(); },
                                ex => { _fsmState = ex is TransientErrorException ? 20 : 200; ContinueFsm(); }
                                );
                            return;
                        }

                    case 30:
                        _flushCounter = _flushAfter;
                        _lastToken = null;
                        _streaming.Setup(_token, _subscriptions.GetHandledTypes().ToArray(), _metadata.ProcessName);
                        SetProcessState(ProcessState.Running);
                        goto case 40;

                    case 40:
                        _tokenToSave = null;
                        goto case 41;

                    case 41:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 200;
                        else
                        {
                            _streaming.GetNextEvent(
                                (tk, evt) => { _fsmState = 50; _token = tk; _handledEvent = evt; ContinueFsm(); },
                                () => { _fsmState = 50; _token = null; _handledEvent = null; ContinueFsm(); },
                                (ex, evt) => { _fsmState = ex is TransientErrorException ? 41 : 200; ContinueFsm(); },
                                _lastToken != null);
                            return;
                        }

                    case 50:
                        if (_handledEvent != null && _processState == ProcessState.Running)
                        {
                            _lastToken = _token;
                            _handlerRetriesLeft = 3;
                            _handler = _subscriptions.FindHandler(_handledEvent.GetType());
                            if (_handler != null)
                            {
                                goto case 52;
                            }
                            else
                            {
                                if (_flushCounter > 0)
                                    _flushCounter--;
                                else
                                    _tokenToSave = _token;
                                goto case 92;
                            }
                        }
                        else if (_lastToken != null)
                        {
                            _tokenToSave = _lastToken;
                            _lastToken = null;
                            goto case 92;
                        }
                        else
                        {
                            goto case 92;
                        }


                    case 52:
                        if (_handlerRetriesLeft > 0)
                            goto case 60;
                        else
                            goto case 70;

                    case 60:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 200;
                        else
                        {
                            try
                            {
                                _handler.Handle(_handledEvent,
                                    () => { _fsmState = 70; ContinueFsm(); },
                                    ex =>
                                    {
                                        _handlerRetriesLeft = (ex is TransientErrorException) ? _handlerRetriesLeft - 1 : 0;
                                        _fsmState = 52;
                                        ContinueFsm();
                                    });
                                return;
                            }
                            catch (TransientErrorException)
                            {
                                _handlerRetriesLeft--;
                                goto case 52;
                            }
                            catch
                            {
                                _handlerRetriesLeft = 0;
                                goto case 52;
                            }
                        }

                    case 70:
                        if (_handlerRetriesLeft == 0)
                            goto case 71;
                        else
                            goto case 80;

                    case 71:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 200;
                        else
                        {
                            _streaming.MarkAsDeadLetter(
                                () => { _fsmState = 80; ContinueFsm(); },
                                ex => { _fsmState = 71; ContinueFsm(); }
                                );
                            return;
                        }

                    case 80:
                        _tokenToSave = _token;
                        goto case 92;

                    case 92:
                        if (_tokenToSave != null)
                            goto case 100;
                        else
                            goto case 40;

                    case 100:
                        if (_processState == ProcessState.Pausing || _processState == ProcessState.Stopping)
                            goto case 200;
                        else
                        {
                            _flushCounter = _flushAfter;
                            _metadata.SetToken(_tokenToSave,
                                () => { _fsmState = 40; ContinueFsm(); },
                                ex => { _fsmState = 100; ContinueFsm(); });
                            return;
                        }

                    case 200:
                        _streaming.Dispose();
                        _metadata.Unlock();
                        goto case 210;

                    case 210:
                        SetProcessState(ProcessState.Inactive);
                        return;
                }
            }
            catch
            {
                if (_fsmState >= 20 && _fsmState <= 200)
                {
                    try { _streaming.Dispose(); }
                    catch { }
                    try { _metadata.Unlock(); }
                    catch { }
                }
                SetProcessState(ProcessState.Inactive);
            }
        }

        public void Dispose()
        {
            SetProcessState(ProcessState.Inactive);
        }

        public EventProcessSimple WithTokenFlushing(int flushAfter)
        {
            _flushAfter = flushAfter > 1 ? flushAfter : 1;
            return this;
        }
    }
}
