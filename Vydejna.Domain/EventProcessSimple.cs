using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IEventHandler
    {
        IList<string> GetEventStreamPrefixes();
    }
    public class EventProcessSimple
    {
        private readonly IMetadataInstance _metadata;
        private readonly IEventStreaming _streaming;
        private readonly IEventSourcedSerializer _serializer;
        private readonly ICommandSubscriptionManager _subscriptions;
        private CancellationTokenSource _cancel;
        private bool _isRunning;
        private EventStoreToken _token;
        private IEventHandler _eventHandler;
        private IEventStreamer _streamer;
        private IEnumerator<ICommandSubscription> _handlerList;
        private object _handledEvent;
        private int _flushCounter;
        private bool _metadataDirty;

        public EventProcessSimple(IMetadataInstance metadata, IEventStreaming streaming, IEventSourcedSerializer serializer, ICommandSubscriptionManager subscriptions)
        {
            _metadata = metadata;
            _streaming = streaming;
            _serializer = serializer;
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
            bus.Subscribe<SystemEvents.SystemInit>(SystemInit);
            bus.Subscribe<SystemEvents.SystemShutdown>(SystemShutdown);
        }

        private void SystemInit(SystemEvents.SystemInit msg)
        {
            _cancel = new CancellationTokenSource();
            _metadata.Lock(ObtainedLock);
        }

        private void SystemShutdown(SystemEvents.SystemShutdown msg)
        {
            _cancel.Cancel();
            if (!_isRunning)
                _metadata.CancelLock();
        }

        private void ObtainedLock()
        {
            _isRunning = true;
            if (!_cancel.IsCancellationRequested)
                _metadata.GetData(OnMetadataLoaded, OnError);
            else
                StopRunning();
        }

        private void OnMetadataLoaded(MetadataInfo handlerInfo)
        {
            if (!_cancel.IsCancellationRequested)
            {
                try
                {
                    _token = handlerInfo.Token;
                    var handledTypeNames = _subscriptions.GetHandledTypes().Select(_serializer.GetTypeName).ToArray();
                    var prefixes = _eventHandler != null ? _eventHandler.GetEventStreamPrefixes() : null;
                    _streamer = _streaming.GetStreamer(new EventStreamingFilter(_token, handledTypeNames, prefixes ?? new string[0]));
                    _streamer.GetNextEvent(EventReceived, _cancel.Token, false);
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
            else
                StopRunning();
        }

        private void EventReceived(EventStoreEvent evnt)
        {
            try
            {
                if (evnt != null && !_cancel.IsCancellationRequested)
                {
                    var eventType = _serializer.GetTypeFromName(evnt.Type);
                    var handler = _subscriptions.FindHandler(eventType);
                    if (handler == null)
                        EventHandled();
                    else
                    {
                        _handledEvent = _serializer.Deserialize(evnt);
                        _token = evnt.Token;
                        _handlerList.Current.Handle(_handledEvent, EventHandled, OnHandlerError);
                    }
                }
                else
                    SaveMetadata();
            }
            catch (Exception exception)
            {
                OnError(exception);
            }
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
                _streamer.GetNextEvent(EventReceived, _cancel.Token, true);
            }
            else
                SaveMetadata();
        }

        private void SaveMetadata()
        {
            if (_metadataDirty)
            {
                _flushCounter = 20;
                _metadata.SetData(new MetadataInfo(_token, string.Empty), OnMetadataSaved, OnError);
            }
            else
                OnMetadataSaved();
        }

        private void OnMetadataSaved()
        {
            if (!_cancel.IsCancellationRequested)
                _streamer.GetNextEvent(EventReceived, _cancel.Token, false);
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
            _metadata.CancelLock();
            _isRunning = false;
        }
    }
}
