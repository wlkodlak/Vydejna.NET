using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class EventSourcedServiceExecution<T>
        where T : class, IEventSourcedAggregate, new()
    {
        private IEventSourcedRepository<T> _repository;
        private IAggregateId _aggregateId;
        private Action<T> _existingAction;
        private Func<T> _newAction;
        private int _tryNumber = 0;
        private Func<CommandError> _validation;
        private Stopwatch _stopwatch;
        private Dictionary<Type, Func<object, string>> _logConverters;
        private ILog _logger;

        public EventSourcedServiceExecution(IEventSourcedRepository<T> repository, IAggregateId aggregateId, ILog logger)
        {
            _repository = repository;
            _aggregateId = aggregateId;
            _tryNumber = 0;
            _stopwatch = new Stopwatch();
            _logConverters = new Dictionary<Type, Func<object, string>>();
            _logger = logger;
        }

        public EventSourcedServiceExecution<T> OnExisting(Action<T> action)
        {
            _existingAction = action;
            return this;
        }

        public EventSourcedServiceExecution<T> OnNew(Func<T> action)
        {
            _newAction = action;
            return this;
        }

        public EventSourcedServiceExecution<T> OnNew(Action<T> action)
        {
            _newAction = () =>
            {
                var agg = new T();
                action(agg);
                return agg;
            };
            return this;
        }

        public EventSourcedServiceExecution<T> OnRequest(Action<T> action)
        {
            return OnNew(action).OnExisting(action);
        }

        public EventSourcedServiceExecution<T> Validate(Func<CommandError> validation)
        {
            _validation = validation;
            return this;
        }

        public Task<CommandResult> Execute()
        {
            _stopwatch.Start();
            return TaskUtils.FromEnumerable<CommandResult>(ExecuteInternal()).GetTask();
        }

        public IEnumerable<Task> ExecuteInternal()
        {
            if (_validation != null)
            {
                var validationResult = _validation();
                if (validationResult != null)
                {
                    yield return TaskUtils.FromResult(CommandResult.From(validationResult));
                    yield break;
                }
            }
            for (_tryNumber = 0; _tryNumber <= 3; _tryNumber++)
            {
                var loadTask = _repository.Load(_aggregateId);
                yield return loadTask;
                Task<CommandResult> result = null;

                var aggregate = loadTask.Result;
                try
                {
                    if (aggregate == null)
                    {
                        if (_newAction != null)
                            aggregate = _newAction();
                        else
                            aggregate = null;
                    }
                    else
                    {
                        if (_existingAction != null)
                            _existingAction(aggregate);
                    }
                }
                catch (DomainErrorException error)
                {
                    result = TaskUtils.FromResult(CommandResult.From(error));
                }
                if (result != null)
                {
                    yield return result;
                    yield break;
                }

                bool aggregateSaved;
                if (aggregate != null)
                {
                    _stopwatch.Stop();
                    var newEvents = aggregate.GetChanges();

                    var saveTask = _repository.Save(aggregate);
                    yield return saveTask;

                    aggregateSaved = saveTask.Result;
                }
                else
                    aggregateSaved = true;
                if (aggregateSaved)
                {
                    yield return CommandResult.TaskOk;
                    yield break;
                }
            }
            yield return TaskUtils.FromError<CommandResult>(new TransientErrorException("CONCURRENCY", "Could not save aggregate"));
        }

    }
}
