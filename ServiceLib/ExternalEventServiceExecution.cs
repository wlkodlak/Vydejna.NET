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
        Task Save(object evnt, string streamName);
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

        public Task Save(object evnt, string streamName)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            var fullStreamName = string.Concat(_streamPrefix, streamName);
            return _eventStore.AddToStream(fullStreamName, new[] { storedEvent }, EventStoreVersion.Any);
        }
    }

    public class ExternalEventServiceExecution
    {
        private readonly IExternalEventRepository _repository;
        private readonly ILog _logger;
        private readonly string _streamName;
        private readonly Stopwatch _stopwatch;
        private readonly List<object> _events;

        public ExternalEventServiceExecution(IExternalEventRepository repository, ILog logger, string streamName)
        {
            _repository = repository;
            _logger = logger;
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
                foreach (var evnt in _events)
                {
                    var taskSave = _repository.Save(evnt, _streamName);
                    yield return taskSave;
                    taskSave.Wait();
                }
                _logger.InfoFormat("Events {0} saved in {1} ms", 
                    string.Join(", ", _events.Select(e => e.GetType().Name)), 
                    _stopwatch.ElapsedMilliseconds);
                yield return CommandResult.TaskOk;
            }
            finally
            {
                _stopwatch.Stop();
            }
        }
    }
}
