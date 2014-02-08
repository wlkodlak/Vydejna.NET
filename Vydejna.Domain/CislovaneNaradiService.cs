﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class CislovaneNaradiService
        : IHandle<CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand>>
        , IHandle<CommandExecution<CislovaneNaradiVydatDoVyrobyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPredatKOpraveCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand>>
    {
        private IEventSourcedRepository<CislovaneNaradi> _repository;
        private ITime _time;

        public CislovaneNaradiService(IEventSourcedRepository<CislovaneNaradi> repository, ITime time)
        {
            _repository = repository;
            _time = time;
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiVydatDoVyrobyCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKOpraveCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand> message)
        {
            new ServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public class ServiceExecutionConcurrencyException : Exception
        {
            public ServiceExecutionConcurrencyException()
                : base("Could not execute command because of constant concurrency failures")
            {
            }
        }

        public class ServiceExecution<T>
            where T : class, IEventSourcedAggregate, new()
        {
            private IEventSourcedRepository<T> _repository;
            private Guid _aggregateId;
            private Action _onComplete;
            private Action<Exception> _onError;
            private Action<T> _existingAction;
            private Func<T> _newAction;
            private int _tryNumber = 0;

            public ServiceExecution(IEventSourcedRepository<T> repository, Guid aggregateId, Action onComplete, Action<Exception> onError)
            {
                _repository = repository;
                _aggregateId = aggregateId;
                _onComplete = onComplete;
                _onError = onError;
                _tryNumber = 0;
            }

            public ServiceExecution<T> OnExisting(Action<T> action)
            {
                _existingAction = action;
                return this;
            }

            public ServiceExecution<T> OnNew(Func<T> action)
            {
                _newAction = action;
                return this;
            }

            public ServiceExecution<T> OnNew(Action<T> action)
            {
                _newAction = () =>
                {
                    var agg = new T();
                    action(agg);
                    return agg;
                };
                return this;
            }

            public ServiceExecution<T> OnRequest(Action<T> action)
            {
                return OnNew(action).OnExisting(action);
            }

            public void Execute()
            {
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
    }
}