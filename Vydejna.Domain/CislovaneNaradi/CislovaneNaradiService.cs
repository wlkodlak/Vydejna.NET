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
        : IHandle<CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand>>
        , IHandle<CommandExecution<CislovaneNaradiVydatDoVyrobyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPredatKOpraveCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand>>
        , IHandle<CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand>>
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
            bus.Subscribe<CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand>>(this);
            bus.Subscribe<CommandExecution<CislovaneNaradiVydatDoVyrobyCommand>>(this);
            bus.Subscribe<CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand>>(this);
            bus.Subscribe<CommandExecution<CislovaneNaradiPredatKOpraveCommand>>(this);
            bus.Subscribe<CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand>>(this);
            bus.Subscribe<CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand>>(this);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiVydatDoVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKOpraveCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command, _time))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }
    }
}
