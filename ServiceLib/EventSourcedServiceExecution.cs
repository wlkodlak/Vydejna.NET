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
        private Action _onComplete;
        private Action<Exception> _onError;
        private Action<T> _existingAction;
        private Func<T> _newAction;
        private int _tryNumber = 0;
        private Func<ValidationErrorException> _validation;

        public EventSourcedServiceExecution(IEventSourcedRepository<T> repository, IAggregateId aggregateId, Action onComplete, Action<Exception> onError)
        {
            _repository = repository;
            _aggregateId = aggregateId;
            _onComplete = onComplete;
            _onError = onError;
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

        public void Execute()
        {
            if (_validation != null)
            {
                var validationResult = _validation();
                if (validationResult != null)
                {
                    _onError(validationResult);
                    return;
                }
            }
            if (_tryNumber <= 3)
            {
                _tryNumber++;
                _repository.Load(_aggregateId, OnAggregateLoaded, OnAggregateMissing, _onError);
            }
            else
                _onError(new ServiceExecutionConcurrencyException());
        }

        private void OnAggregateLoaded(T aggregate)
        {
            try
            {
                _existingAction(aggregate);
                _repository.Save(aggregate, _onComplete, Execute, _onError);
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        private void OnAggregateMissing()
        {
            try
            {
                var aggregate = _newAction();
                _repository.Save(aggregate, _onComplete, Execute, _onError);
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }
    }

    public class ServiceExecutionConcurrencyException : Exception
    {
        public ServiceExecutionConcurrencyException()
            : base("Could not execute command because of constant concurrency failures")
        {
        }
    }
}
