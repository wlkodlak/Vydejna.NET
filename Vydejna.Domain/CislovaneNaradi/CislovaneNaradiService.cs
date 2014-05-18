using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain.CislovaneNaradi
{
    public class CislovaneNaradiService
        : IProcess<CislovaneNaradiPrijmoutNaVydejnuCommand>
        , IProcess<CislovaneNaradiVydatDoVyrobyCommand>
        , IProcess<CislovaneNaradiPrijmoutZVyrobyCommand>
        , IProcess<CislovaneNaradiPredatKOpraveCommand>
        , IProcess<CislovaneNaradiPrijmoutZOpravyCommand>
        , IProcess<CislovaneNaradiPredatKeSesrotovaniCommand>
    {
        private IEventSourcedRepository<CislovaneNaradiAggregate> _repository;
        private ITime _time;
        private CislovaneNaradiValidation _validator;

        public CislovaneNaradiService(IEventSourcedRepository<CislovaneNaradiAggregate> repository, ITime time)
        {
            _repository = repository;
            _time = time;
            _validator = new CislovaneNaradiValidation();
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<CislovaneNaradiPrijmoutNaVydejnuCommand>(this);
            bus.Subscribe<CislovaneNaradiVydatDoVyrobyCommand>(this);
            bus.Subscribe<CislovaneNaradiPrijmoutZVyrobyCommand>(this);
            bus.Subscribe<CislovaneNaradiPredatKOpraveCommand>(this);
            bus.Subscribe<CislovaneNaradiPrijmoutZOpravyCommand>(this);
            bus.Subscribe<CislovaneNaradiPredatKeSesrotovaniCommand>(this);
        }

        public Task Handle(CislovaneNaradiPrijmoutNaVydejnuCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(CislovaneNaradiVydatDoVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(CislovaneNaradiPrijmoutZVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(CislovaneNaradiPredatKOpraveCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message, _time))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(CislovaneNaradiPrijmoutZOpravyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(CislovaneNaradiPredatKeSesrotovaniCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }
    }
}
