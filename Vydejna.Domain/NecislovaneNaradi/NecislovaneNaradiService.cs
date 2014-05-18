using ServiceLib;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class NecislovaneNaradiService
        : IProcess<NecislovaneNaradiPrijmoutNaVydejnuCommand>
        , IProcess<NecislovaneNaradiVydatDoVyrobyCommand>
        , IProcess<NecislovaneNaradiPrijmoutZVyrobyCommand>
        , IProcess<NecislovaneNaradiPredatKOpraveCommand>
        , IProcess<NecislovaneNaradiPrijmoutZOpravyCommand>
        , IProcess<NecislovaneNaradiPredatKeSesrotovaniCommand>
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

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<NecislovaneNaradiPrijmoutNaVydejnuCommand>(this);
            bus.Subscribe<NecislovaneNaradiVydatDoVyrobyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPrijmoutZVyrobyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPredatKOpraveCommand>(this);
            bus.Subscribe<NecislovaneNaradiPrijmoutZOpravyCommand>(this);
            bus.Subscribe<NecislovaneNaradiPredatKeSesrotovaniCommand>(this);
        }

        public Task Handle(NecislovaneNaradiPrijmoutNaVydejnuCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(NecislovaneNaradiVydatDoVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(NecislovaneNaradiPrijmoutZVyrobyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(NecislovaneNaradiPredatKOpraveCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message, _time))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(NecislovaneNaradiPrijmoutZOpravyCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }

        public Task Handle(NecislovaneNaradiPredatKeSesrotovaniCommand message)
        {
            return new EventSourcedServiceExecution<NecislovaneNaradiAggregate>(_repository, message.NaradiId.ToId())
                .Validate(() => _validator.Validace(message))
                .OnRequest(agg => agg.Execute(message, _time))
                .Execute();
        }
    }
}
