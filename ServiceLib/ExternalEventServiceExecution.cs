using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

        public Task Save(object evnt, string streamName, IEventProcessTrackSource tracker)
        {
            return TaskUtils.FromEnumerable(SaveInternal(evnt, streamName, tracker)).GetTask();
        }

        public IEnumerable<Task> SaveInternal(object evnt, string streamName, IEventProcessTrackSource tracker)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            var fullStreamName = string.Concat(_streamPrefix, streamName);
            var taskAdd = _eventStore.AddToStream(fullStreamName, new[] { storedEvent }, EventStoreVersion.Any);
            yield return taskAdd;

            if (taskAdd.Result)
            {
                tracker.AddEvent(storedEvent.Token);
            }
        }
    }

    public class ExternalEventServiceExecution
    {
        private readonly IExternalEventRepository _repository;
        private readonly ILog _logger;
        private readonly string _streamName;
        private readonly Stopwatch _stopwatch;
        private readonly List<object> _events;
        private readonly IEventProcessTrackCoordinator _tracking;

        public ExternalEventServiceExecution(IExternalEventRepository repository, ILog logger, IEventProcessTrackCoordinator tracking, string streamName)
        {
            _repository = repository;
            _logger = logger;
            _tracking = tracking;
            _streamName = streamName;
            _stopwatch = new Stopwatch();
            _events = new List<object>();
        }

        public ExternalEventServiceExecution AddEvent(object evnt)
        {
            _events.Add(evnt);
            return this;
        }

        public Task<CommandResult> Execute()
        {
            return TaskUtils.FromEnumerable<CommandResult>(ExecuteInternal()).GetTask();
        }

        private IEnumerable<Task> ExecuteInternal()
        {
            _stopwatch.Start();
            try
            {
                var tracker = _tracking.CreateTracker();
                foreach (var evnt in _events)
                {
                    var taskSave = _repository.Save(evnt, _streamName, tracker);
                    yield return taskSave;
                    taskSave.Wait();
                }
                _logger.InfoFormat("Events {0} saved in {1} ms", 
                    string.Join(", ", _events.Select(e => e.GetType().Name)), 
                    _stopwatch.ElapsedMilliseconds);
                yield return TaskUtils.FromResult(CommandResult.Success(tracker.TrackingId));
            }
            finally
            {
                _stopwatch.Stop();
            }
        }
    }
}
