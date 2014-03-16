using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class NecislovaneNaradiService
        : IHandle<CommandExecution<NecislovaneNaradiPrijmoutNaVydejnuCommand>>
        , IHandle<CommandExecution<NecislovaneNaradiVydatDoVyrobyCommand>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijmoutZVyrobyCommand>>
        , IHandle<CommandExecution<NecislovaneNaradiPredatKOpraveCommand>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijmoutZOpravyCommand>>
        , IHandle<CommandExecution<NecislovaneNaradiPredatKeSesrotovaniCommand>>
    {
        private IEventSourcedRepository<NecislovaneNaradiAggregate> _repository;
        private ITime _time;
        private NecislovaneNaradiValidation _validator;

        public NecislovaneNaradiService(IEventSourcedRepository<NecislovaneNaradiAggregate> repository, ITime time)
        {
            _repository = repository;
            _time = time;
            _validator = new NecislovaneNaradiValidation();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijmoutNaVydejnuCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydatDoVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijmoutZVyrobyCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredatKOpraveCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command, _time))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijmoutZOpravyCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredatKeSesrotovaniCommand> message)
        {
            new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.Command.NaradiId.ToId(), message.OnCompleted, message.OnError)
                .Validate(() => _validator.Validace(message.Command))
                .OnRequest(agg => agg.Execute(message.Command, _time))
                .Execute();
        }
    }
}
