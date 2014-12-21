using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IExternalEventRepository
    {
        Task Save(object evnt, string streamName, IEventProcessTrackSource tracker);
    }

    public class ExternalEventRepository : IExternalEventRepository
    {
        private readonly IEventStore _eventStore;
        private readonly string _streamPrefix;
        private readonly IEventSourcedSerializer _serializer;

        public ExternalEventRepository(IEventStore eventStore, string streamPrefix, IEventSourcedSerializer serializer)
        {
            _eventStore = eventStore;
            _streamPrefix = streamPrefix;
            _serializer = serializer;
        }

        public async Task Save(object evnt, string streamName, IEventProcessTrackSource tracker)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            var fullStreamName = string.Concat(_streamPrefix, streamName);
            var addedToStream = await _eventStore.AddToStream(fullStreamName, new[] { storedEvent }, EventStoreVersion.Any);
            if (addedToStream)
            {
                tracker.AddEvent(storedEvent.Token);
            }
        }
    }

    public class ExternalEventServiceExecution
    {
        private readonly IExternalEventRepository _repository;
        private readonly ExternalEventServiceExecutionTraceSource _logger;
        private readonly string _streamName;
        private readonly List<object> _events;
        private readonly IEventProcessTrackCoordinator _tracking;

        public ExternalEventServiceExecution(IExternalEventRepository repository, ExternalEventServiceExecutionTraceSource logger, IEventProcessTrackCoordinator tracking, string streamName)
        {
            _repository = repository;
            _logger = logger;
            _tracking = tracking;
            _streamName = streamName;
            _events = new List<object>();
        }

        public ExternalEventServiceExecution AddEvent(object evnt)
        {
            _events.Add(evnt);
            return this;
        }

        public async Task<CommandResult> Execute()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var tracker = _tracking.CreateTracker();
                foreach (var evnt in _events)
                {
                    await _repository.Save(evnt, _streamName, tracker);
                }
                _logger.EventsSaved(_events, stopwatch.ElapsedMilliseconds);
                return CommandResult.Success(tracker.TrackingId);
            }
            finally
            {
                stopwatch.Stop();
            }
        }
    }

    public class ExternalEventServiceExecutionTraceSource : TraceSource
    {
        public ExternalEventServiceExecutionTraceSource(string name)
            : base(name)
        {
        }

        public void EventsSaved(List<object> events, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "{EventsCount} events saved");
            msg.SetProperty("EventsCount", false, events.Count);
            msg.SetProperty("DurationMs", false, elapsedMilliseconds);
            msg.Log(this);
        }
    }
}
