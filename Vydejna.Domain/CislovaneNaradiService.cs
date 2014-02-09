using System;
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
        private CislovaneNaradiValidation _validator;

        public CislovaneNaradiService(IEventSourcedRepository<CislovaneNaradi> repository, ITime time)
        {
            _repository = repository;
            _time = time;
            _validator = new CislovaneNaradiValidation();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutNaVydejnuCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiVydatDoVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKOpraveCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command, _time))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijmoutZOpravyCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredatKeSesrotovaniCommand> message)
        {
            new EventSourcedServiceExecution<CislovaneNaradi>(_repository, message.Command.NaradiId, message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }
    }
}
