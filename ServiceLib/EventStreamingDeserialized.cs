using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IEventStreamingDeserialized
    {
        void Setup(EventStoreToken firstToken, IEnumerable<Type> types, string processName);
        Task<EventStreamingDeserializedEvent> GetNextEvent(bool nowait);
        Task MarkAsDeadLetter();
        void Close();
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
        private static readonly EventStreamingDeserializedTraceSource Logger
            = new EventStreamingDeserializedTraceSource("ServiceLib.EventStreamingDeserialized");
        private readonly IEventStreaming _streaming;
        private readonly IEventSourcedSerializer _serializer;
        private IEventStreamer _streamer;
        private HashSet<string> _typeFilter;
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
            Logger.SetupComplete(processName, firstToken, _typeFilter.ToList());
        }

        public async Task<EventStreamingDeserializedEvent> GetNextEvent(bool nowait)
        {
            while (true)
            {
                var nextEvent = await _streamer.GetNextEvent(nowait);
                if (nextEvent == null)
                {
                    Logger.NoNewEvents(_processName);
                    return null;
                }
                if (!_typeFilter.Contains(nextEvent.Type))
                {
                    Logger.SkippedEvent(_processName, nextEvent);
                    continue;
                }
                try
                {
                    var deserialized = _serializer.Deserialize(nextEvent);
                    Logger.ReturnedEvent(_processName, nextEvent, deserialized);
                    return new EventStreamingDeserializedEvent(nextEvent, deserialized);
                }
                catch (Exception exception)
                {
                    Logger.ReturnedError(_processName, nextEvent, exception);
                    return new EventStreamingDeserializedEvent(nextEvent, exception);
                }
            }
        }

        public Task MarkAsDeadLetter()
        {
            return _streamer.MarkAsDeadLetter();
        }

        public void Close()
        {
            if (_streamer != null)
                _streamer.Dispose();
        }
    }

    public class EventStreamingDeserializedTraceSource : TraceSource
    {
        public EventStreamingDeserializedTraceSource(string name)
            : base(name)
        {
        }

        public void SetupComplete(string processName, EventStoreToken firstToken, IList<string> allowedTypes)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Completed setup for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("FirstToken", false, firstToken);
            msg.SetProperty("AllowedTypes", true, AllowedTypesList(allowedTypes));
            msg.Log(this);
        }

        private static string AllowedTypesList(ICollection<string> allowedTypes)
        {
            if (allowedTypes == null || allowedTypes.Count == 0)
                return "(none)";
            var numberOfTypes = 0;
            var builder = new StringBuilder();
            foreach (var allowedType in allowedTypes)
            {
                if (numberOfTypes >= 24)
                {
                    builder.Append(", ...");
                    break;
                }
                if (numberOfTypes > 0)
                    builder.Append(", ");
                builder.Append(allowedType);
                numberOfTypes++;
            }
            return builder.ToString();
        }

        public void NoNewEvents(string processName)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "No new events for {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.Log(this);
        }

        public void SkippedEvent(string processName, EventStoreEvent nextEvent)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 3, "Skipping event {Token} of type {EventType} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, nextEvent.Token);
            msg.SetProperty("EventType", false, nextEvent.Type);
            msg.Log(this);
        }

        public void ReturnedEvent(string processName, EventStoreEvent rawEvent, object deserialized)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 3, "Returning event {Token} of type {EventType} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, rawEvent.Token);
            msg.SetProperty("EventType", false, rawEvent.Type);
            msg.Log(this);
        }

        public void ReturnedError(string processName, EventStoreEvent rawEvent, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 3, "Failed to deserialize event {Token} of type {EventType} for process {ProcessName}");
            msg.SetProperty("ProcessName", false, processName);
            msg.SetProperty("Token", false, rawEvent.Token);
            msg.SetProperty("EventType", false, rawEvent.Type);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }
}
