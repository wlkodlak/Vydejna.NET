using log4net;
using ServiceLib;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class NecislovaneNaradiService
        : IProcessCommand<NecislovaneNaradiPrijmoutNaVydejnuCommand>
        , IProcessCommand<NecislovaneNaradiVydatDoVyrobyCommand>
        , IProcessCommand<NecislovaneNaradiPrijmoutZVyrobyCommand>
        , IProcessCommand<NecislovaneNaradiPredatKOpraveCommand>
        , IProcessCommand<NecislovaneNaradiPrijmoutZOpravyCommand>
        , IProcessCommand<NecislovaneNaradiPredatKeSesrotovaniCommand>
    {
        private IEventSourcedRepository<NecislovaneNaradiAggregate> _repository;
        private ITime _time;
        private NecislovaneNaradiValidation _validator;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.NecislovaneNaradi");

        public NecislovaneNaradiService(IEventSourcedRepository<NecislovaneNaradiAggregate> repository, ITime time)
        {
            _repository = repository;
            _time = time;
            _validator = new NecislovaneNaradiValidation();
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<NecislovaneNaradiPrijmoutNaVydejnuCommand>(this);
            bus.Subscribe<NecislovaneNaradiVydatDoVyrobyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPrijmoutZVyrobyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPredatKOpraveCommand>(this);
            bus.Subscribe<NecislovaneNaradiPrijmoutZOpravyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPredatKeSesrotovaniCommand>(this);
        }

        public Task<CommandResult> Handle(NecislovaneNaradiPrijmoutNaVydejnuCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(NecislovaneNaradiVydatDoVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(NecislovaneNaradiPrijmoutZVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(NecislovaneNaradiPredatKOpraveCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message, _time))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(NecislovaneNaradiPrijmoutZOpravyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task<CommandResult> Handle(NecislovaneNaradiPredatKeSesrotovaniCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId(), Logger)
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }
    }
}
