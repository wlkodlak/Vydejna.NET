using System;
using System.Collections.Generic;
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
        private Func<ValidationErrorException> _validation;

        public EventSourcedServiceExecution(IEventSourcedRepository<T> repository, IAggregateId aggregateId)
        {
            _repository = repository;
            _aggregateId = aggregateId;
            _tryNumber = 0;
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

        public EventSourcedServiceExecution<T> Validate(Func<ValidationErrorException> validation)
        {
            _validation = validation;
            return this;
        }

        public Task Execute()
        {
            return TaskUtils.FromEnumerable<object>(ExecuteInternal()).GetTask();
        }

        public IEnumerable<Task> ExecuteInternal()
        {
            if (_validation != null)
            {
                var validationResult = _validation();
                if (validationResult != null)
                {
                    yield return TaskUtils.FromError<object>(validationResult);
                    yield break;
                }
                for (_tryNumber = 0; _tryNumber <= 3; _tryNumber++)
                {
                    var loadTask = _repository.Load(_aggregateId);
                    yield return loadTask;

                    var aggregate = loadTask.Result;
                    if (aggregate == null)
                        _newAction();
                    else
                        _existingAction(aggregate);
                    var saveTask = _repository.Save(aggregate);
                    yield return saveTask;

                    if (saveTask.Result)
                    {
                        yield return TaskUtils.CompletedTask();
                        yield break;
                    }
                }
                yield return TaskUtils.FromError<object>(new TransientErrorException("CONCURRENCY", "Could not save aggregate", _tryNumber));
            }
        }
    }
}
