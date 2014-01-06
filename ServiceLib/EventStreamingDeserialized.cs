using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib
{
    public interface IEventStreamingDeserialized : IDisposable
    {
        void Setup(EventStoreToken firstToken, IList<Type> types);
        void GetNextEvent(Action<EventStoreToken, object> onEventRead, Action onEventNotAvailable, Action<Exception, EventStoreEvent> onError, bool nowait);
    }

    public class EventStreamingDeserialized : IEventStreamingDeserialized
    {
        private IEventStreaming _streaming;
        private IEventSourcedSerializer _serializer;
        private IEventStreamer _streamer;
        private HashSet<string> _typeFilter;
        private Action<EventStoreToken, object> _onEventRead;
        private Action _onEventNotAvailable;
        private Action<Exception, EventStoreEvent> _onError;
        private bool _nowait, _isDisposed;

        public EventStreamingDeserialized(IEventStreaming streaming, IEventSourcedSerializer serializer)
        {
            _streaming = streaming;
            _serializer = serializer;
        }

        public void Setup(EventStoreToken firstToken, IList<Type> types)
        {
            _typeFilter = new HashSet<string>(types.Select(_serializer.GetTypeName));
            _streamer = _streaming.GetStreamer(firstToken);
            _isDisposed = false;
        }

        public void GetNextEvent(Action<EventStoreToken, object> onEventRead, Action onEventNotAvailable, Action<Exception, EventStoreEvent> onError, bool nowait)
        {
            _onEventRead = onEventRead;
            _onEventNotAvailable = onEventNotAvailable;
            _onError = onError;
            _nowait = nowait;
            if (_isDisposed)
                _onError(new ObjectDisposedException("Streamer is disposed"), null);
            else
                _streamer.GetNextEvent(RawEventReceived, OnError, _nowait);
        }

        public void Dispose()
        {
            _isDisposed = true;
            _streamer.Dispose();
        }

        private void OnError(Exception exception)
        {
            _onError(exception, null);
        }

        private void RawEventReceived(EventStoreEvent rawEvent)
        {
            if (rawEvent == null)
                _onEventNotAvailable();
            else
            {
                if (_typeFilter.Contains(rawEvent.Type))
                {
                    object deserialized;
                    try
                    {
                        deserialized = _serializer.Deserialize(rawEvent);
                    }
                    catch (Exception exception)
                    {
                        _onError(exception, rawEvent);
                        return;
                    }
                    _onEventRead(rawEvent.Token, deserialized);
                }
                else
                    _streamer.GetNextEvent(RawEventReceived, OnError, _nowait);
            }
        }
    }

}
