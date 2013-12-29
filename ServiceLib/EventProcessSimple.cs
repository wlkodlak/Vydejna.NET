using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ServiceLib
{
    public interface IEventHandler
    {
        IList<string> GetEventStreamPrefixes();
    }
    public class EventProcessSimple
        : IHandle<SystemEvents.SystemInit>
        , IHandle<SystemEvents.SystemShutdown>
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreamingDeserialized _streaming;
        private readonly ICommandSubscriptionManager _subscriptions;
        private CancellationTokenSource _cancel;
        private EventStoreToken _token;
        private IEventHandler _eventHandler;
        private object _handledEvent;
        private int _flushCounter;
        private bool _metadataDirty;
        private IDisposable _waitForLock;

        public EventProcessSimple(IMetadataInstance metadata, IEventStreamingDeserialized streaming, ICommandSubscriptionManager subscriptions)
        {
            _metadata = metadata;
            _streaming = streaming;
            _subscriptions = subscriptions;
        }

        public void SetupHandler(IEventHandler eventHandler)
        {
            _eventHandler = eventHandler;
        }

        public IHandleRegistration<CommandExecution<T>> Register<T>(IHandle<CommandExecution<T>> handler)
        {
            return _subscriptions.Register(handler);
        }

        public void Subscribe(IBus bus)
        {
            bus.Subscribe<SystemEvents.SystemInit>(this);
            bus.Subscribe<SystemEvents.SystemShutdown>(this);
        }

        public void Handle(SystemEvents.SystemInit msg)
        {
            _cancel = new CancellationTokenSource();
            _waitForLock = _metadata.Lock(ObtainedLock);
        }

        public void Handle(SystemEvents.SystemShutdown msg)
        {
            _cancel.Cancel();
            _waitForLock.Dispose();
        }

        private void ObtainedLock()
        {
            if (!_cancel.IsCancellationRequested)
                _metadata.GetToken(OnMetadataLoaded, OnError);
            else
                StopRunning();
        }

        private void OnMetadataLoaded(EventStoreToken token)
        {
            if (!_cancel.IsCancellationRequested)
            {
                try
                {
                    _token = token;
                    var prefixes = _eventHandler != null ? _eventHandler.GetEventStreamPrefixes() : null;
                    _streaming.Setup(_token, _subscriptions.GetHandledTypes().ToArray(), prefixes ?? new string[0], false);
                    _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, _cancel.Token, false);
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
            else
                StopRunning();
        }

        private void EventReceived(EventStoreToken token, object evnt)
        {
            try
            {
                if (_cancel.IsCancellationRequested)
                    SaveToken();
                else
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
            }
            catch (Exception exception)
            {
                OnError(exception);
            }
        }

        private void NoNewEvents()
        {
            SaveToken();
        }

        private void CannotReceiveEvents(Exception exception, EventStoreEvent evnt)
        {
            OnError(exception);
        }

        private void OnHandlerError(Exception exception)
        {
            EventHandled();
        }

        private void EventHandled()
        {
            _metadataDirty = true;
            if (_flushCounter > 0)
            {
                _flushCounter--;
                _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, _cancel.Token, true);
            }
            else
                SaveToken();
        }

        private void SaveToken()
        {
            if (_metadataDirty)
            {
                _flushCounter = 20;
                _metadata.SetToken(_token, OnTokenSaved, OnError);
            }
            else
                OnTokenSaved();
        }

        private void OnTokenSaved()
        {
            if (!_cancel.IsCancellationRequested)
                _streaming.GetNextEvent(EventReceived, NoNewEvents, CannotReceiveEvents, _cancel.Token, false);
            else
                StopRunning();
        }

        private void OnError(Exception exception)
        {
            _cancel.Cancel();
            StopRunning();
        }

        private void StopRunning()
        {
            _metadata.Unlock();
        }
    }
}
