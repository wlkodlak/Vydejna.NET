using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventStreamingDeserialized : IDisposable
    {
        void Setup(EventStoreToken firstToken, IEnumerable<Type> types, string processName);
        Task<EventStreamingDeserializedEvent> GetNextEvent(bool nowait);
        Task MarkAsDeadLetter();
    }

    public class EventStreamingDeserializedEvent
    {
        public readonly EventStoreToken Token;
        public readonly object Event;
        public readonly EventStoreEvent Raw;
        public readonly Exception DeserializationException;

        public EventStreamingDeserializedEvent(EventStoreEvent raw, object deserialized)
        {
            Raw = raw;
            Event = deserialized;
            Token = raw.Token;
        }

        public EventStreamingDeserializedEvent(EventStoreEvent raw, Exception error)
        {
            Raw = raw;
            DeserializationException = error;
            Token = raw.Token;
        }
    }

    public class EventStreamingDeserialized : IEventStreamingDeserialized
    {
        private IEventStreaming _streaming;
        private IEventSourcedSerializer _serializer;
        private IEventStreamer _streamer;
        private HashSet<string> _typeFilter;
        private bool _nowait, _isDisposed;
        private string _processName;

        public EventStreamingDeserialized(IEventStreaming streaming, IEventSourcedSerializer serializer)
        {
            _streaming = streaming;
            _serializer = serializer;
        }

        public void Setup(EventStoreToken firstToken, IEnumerable<Type> types, string processName)
        {
            _typeFilter = new HashSet<string>(types.Select(_serializer.GetTypeName));
            _streamer = _streaming.GetStreamer(firstToken, processName);
            _processName = processName;
            _isDisposed = false;
        }

        public Task<EventStreamingDeserializedEvent> GetNextEvent(bool nowait)
        {
            if (_isDisposed)
                return TaskUtils.FromError<EventStreamingDeserializedEvent>(new ObjectDisposedException(_processName ?? "EventStreamingDeserialized"));
            else
            {
                _nowait = nowait;
                return GetNextEvent();
            }
        }

        private Task<EventStreamingDeserializedEvent> GetNextEvent()
        {
            return _streamer.GetNextEvent(_nowait).ContinueWith<Task<EventStreamingDeserializedEvent>>(ProcessEvent).Unwrap();
        }

        private Task<EventStreamingDeserializedEvent> ProcessEvent(Task<EventStoreEvent> taskGetEvent)
        {
            var rawEvent = taskGetEvent.Result;
            if (rawEvent == null)
                return TaskUtils.FromResult<EventStreamingDeserializedEvent>(null);
            else if (!_typeFilter.Contains(rawEvent.Type))
                return GetNextEvent();
            else
            {
                try
                {
                    var deserialized = _serializer.Deserialize(rawEvent);
                    return TaskUtils.FromResult(new EventStreamingDeserializedEvent(rawEvent, deserialized));
                }
                catch (Exception ex)
                {
                    return TaskUtils.FromResult(new EventStreamingDeserializedEvent(rawEvent, ex));
                }
            }
        }

        public Task MarkAsDeadLetter()
        {
            return _streamer.MarkAsDeadLetter();
        }

        public void Dispose()
        {
            _isDisposed = true;
            if (_streamer != null)
                _streamer.Dispose();
        }
    }

}
