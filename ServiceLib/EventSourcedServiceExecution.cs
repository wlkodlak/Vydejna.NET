using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class EventSourcedServiceExecution<T>
        where T : class, IEventSourcedAggregate, new()
    {
        private readonly IEventSourcedRepository<T> _repository;
        private readonly IAggregateId _aggregateId;
        private readonly EventSourcedServiceExecutionTraceSource _logger;
        private Action<T> _existingAction;
        private Func<T> _newAction;
        private int _tryNumber;
        private Func<CommandError> _validation;
        private readonly Stopwatch _stopwatch;
        private readonly IEventProcessTrackCoordinator _tracking;
        private object _command;
        private IEventProcessTrackSource _tracker;
        private T _aggregate;
        private IList<object> _newEvents;

        public EventSourcedServiceExecution(
            IEventSourcedRepository<T> repository, IAggregateId aggregateId,
            EventSourcedServiceExecutionTraceSource logger, IEventProcessTrackCoordinator tracking)
        {
            _repository = repository;
            _aggregateId = aggregateId;
            _logger = logger ?? new EventSourcedServiceExecutionTraceSource("ServiceLib.EventSourcedServiceExecution");
            _tryNumber = 0;
            _stopwatch = new Stopwatch();
            _tracking = tracking;
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

        public EventSourcedServiceExecution<T> LogCommand(object command)
        {
            _command = command;
            return this;
        }

        public async Task<CommandResult> Execute()
        {
            _stopwatch.Start();

            var validationResult = Validate();
            if (validationResult != null) 
                return validationResult;

            try
            {
                try
                {
                    for (_tryNumber = 0; _tryNumber <= 3; _tryNumber++)
                    {
                        _tracker = _tracking.CreateTracker();
                        _aggregate = await _repository.Load(_aggregateId);
                        ProcessCommandInAggregate();

                        if (_aggregate == null)
                            return ReturnSuccessWithoutEvents();
                        AssertSameAggregateId(_aggregate);
                        _newEvents = _aggregate.GetChanges();

                        var aggregateSaved = await _repository.Save(_aggregate, _tracker);
                        if (aggregateSaved)
                            return ReturnSuccess();
                    }
                }
                finally
                {
                    _stopwatch.Stop();
                }
                return ReturnConcurrencyError();
            }
            catch (DomainErrorException error)
            {
                return ReturnDomainError(error);
            }
            catch (Exception error)
            {
                return CommandResult.From(error);
            }
        }

        private CommandResult Validate()
        {
            if (_validation == null) return null;
            var validationResult = _validation();
            if (validationResult == null) return null;
            _logger.ValidationFailed(_aggregateId, _command, validationResult);
            return CommandResult.From(validationResult);
        }

        private void ProcessCommandInAggregate()
        {
            if (_aggregate == null)
            {
                if (_newAction != null)
                    _aggregate = _newAction();
            }
            else
            {
                if (_existingAction != null)
                    _existingAction(_aggregate);
            }
        }

        private void AssertSameAggregateId(T aggregate)
        {
            if (!_aggregateId.Equals(aggregate.Id))
            {
                throw new InvalidOperationException(
                    string.Format(
                        "Saved aggregate has different ID ({0}) than original ({1})",
                        aggregate.Id, _aggregateId));
            }
        }

        private CommandResult ReturnSuccessWithoutEvents()
        {
            _logger.NothingNeededSaving(_aggregateId, _command, _stopwatch.ElapsedMilliseconds);
            return CommandResult.Success(_tracker.TrackingId);
        }

        private CommandResult ReturnSuccess()
        {
            _logger.CommandFinished(_aggregateId, _command, _newEvents, _stopwatch.ElapsedMilliseconds, _tracker.TrackingId);
            return CommandResult.Success(_tracker.TrackingId);
        }

        private CommandResult ReturnConcurrencyError()
        {
            _logger.ConcurrencyFailed(_aggregateId, _command, _stopwatch.ElapsedMilliseconds);
            return CommandResult.From(new TransientErrorException("CONCURRENCY", "Could not save aggregate"));
        }

        private CommandResult ReturnDomainError(DomainErrorException error)
        {
            _logger.DomainErrorOccurred(_aggregateId, _command, _stopwatch.ElapsedMilliseconds, error);
            return CommandResult.From(error);
        }
    }

    public class EventSourcedServiceExecutionTraceSource : TraceSource
    {
        public EventSourcedServiceExecutionTraceSource(string name)
            : base(name)
        {
        }

        public virtual void ValidationFailed(IAggregateId aggregateId, object command, CommandError validationResult)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 11, "Command {CommandType} is invalid");
            msg.SetProperty("CommandType", false, command.GetType().Name);
            msg.SetProperty("AggregateId", false, aggregateId);
            msg.SetProperty("Field", false, validationResult.Field);
            msg.SetProperty("Category", false, validationResult.Category);
            msg.SetProperty("Message", false, validationResult.Message);
            IncludeCommandInMessage(msg, command);
            msg.Log(this);
        }

        public void CommandFinished(IAggregateId aggregateId, object command, IList<object> newEvents, long elapsedMilliseconds, string trackingId)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 1, "Command {CommandType} finished without producing new aggregate");
            msg.SetProperty("CommandType", false, command.GetType().Name);
            msg.SetProperty("AggregateId", false, aggregateId);
            msg.SetProperty("ProcessingTime", false, elapsedMilliseconds);
            msg.SetProperty("TrackingId", false, trackingId);
            IncludeCommandInMessage(msg, command);
            IncludeNewEventsInMessage(msg, newEvents);
            msg.Log(this);
        }

        public void NothingNeededSaving(IAggregateId aggregateId, object command, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 8, "Command {CommandType} finished without producing new aggregate");
            msg.SetProperty("CommandType", false, command.GetType().Name);
            msg.SetProperty("ProcessingTime", false, elapsedMilliseconds);
            IncludeCommandInMessage(msg, command);
            msg.Log(this);
        }

        public void ConcurrencyFailed(IAggregateId aggregateId, object command, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 12, "Command {CommandType} was not completed because of repeated concurrency failures");
            msg.SetProperty("CommandType", false, command.GetType().Name);
            msg.SetProperty("ProcessingTime", false, elapsedMilliseconds);
            IncludeCommandInMessage(msg, command);
            msg.Log(this);
        }

        public void DomainErrorOccurred(IAggregateId aggregateId, object command, long elapsedMilliseconds, DomainErrorException error)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 20, "Command {CommandType} failed - {Message}");
            msg.SetProperty("CommandType", false, command.GetType().Name);
            msg.SetProperty("AggregateId", false, aggregateId);
            msg.SetProperty("ProcessingTime", false, elapsedMilliseconds);
            msg.SetProperty("Field", false, error.Field);
            msg.SetProperty("Category", false, error.Category);
            msg.SetProperty("Message", false, error.Message);
            IncludeCommandInMessage(msg, command);
            msg.Log(this);
        }

        protected virtual void IncludeCommandInMessage(LogContextMessage msg, object command)
        {
        }

        protected virtual void IncludeNewEventsInMessage(LogContextMessage msg, IList<object> newEvents)
        {
            msg.SetProperty("NewEvents", false, string.Join(", ", newEvents.Select(evnt => evnt.GetType().Name)));
        }
    }
}
