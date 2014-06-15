using log4net;
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
        : IProcessCommand<CislovaneNaradiPrijmoutNaVydejnuCommand>
        , IProcessCommand<CislovaneNaradiVydatDoVyrobyCommand>
        , IProcessCommand<CislovaneNaradiPrijmoutZVyrobyCommand>
        , IProcessCommand<CislovaneNaradiPredatKOpraveCommand>
        , IProcessCommand<CislovaneNaradiPrijmoutZOpravyCommand>
        , IProcessCommand<CislovaneNaradiPredatKeSesrotovaniCommand>
    {
        private IEventSourcedRepository<CislovaneNaradiAggregate> _repository;
        private ITime _time;
        private CislovaneNaradiValidation _validator;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.CislovaneNaradi");

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

        public Task<CommandResult> Handle(CislovaneNaradiPrijmoutNaVydejnuCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(CislovaneNaradiVydatDoVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(CislovaneNaradiPrijmoutZVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(CislovaneNaradiPredatKOpraveCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message, _time))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(CislovaneNaradiPrijmoutZOpravyCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(CislovaneNaradiPredatKeSesrotovaniCommand message)
        {
            return new EventSourcedServiceExecution<CislovaneNaradiAggregate>(
                _repository, new CislovaneNaradiId(message.NaradiId, message.CisloNaradi), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }
    }
}
