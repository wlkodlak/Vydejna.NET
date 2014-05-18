using System;
using System.Collections.Generic;
using ServiceLib;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Domain.ExterniCiselniky
{
    public class ExterniCiselnikyService
        : IProcess<DefinovanDodavatelEvent>
        , IProcess<DefinovanaVadaNaradiEvent>
        , IProcess<DefinovanoPracovisteEvent>
    {
        private IExterniCiselnikyRepository _repository;

        public ExterniCiselnikyService(IExterniCiselnikyRepository repository)
        {
            _repository = repository;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DefinovanDodavatelEvent>(this);
            bus.Subscribe<DefinovanaVadaNaradiEvent>(this);
            bus.Subscribe<DefinovanoPracovisteEvent>(this);
        }

        public Task Handle(DefinovanDodavatelEvent msg)
        {
            return _repository.UlozitDodavatele(msg);
        }
        public Task Handle(DefinovanaVadaNaradiEvent msg)
        {
            return _repository.UlozitVadu(msg);
        }
        public Task Handle(DefinovanoPracovisteEvent msg)
        {
            return _repository.UlozitPracoviste(msg);
        }
    }

    public interface IExterniCiselnikyRepository
    {
        Task UlozitDodavatele(DefinovanDodavatelEvent evnt);
        Task UlozitVadu(DefinovanaVadaNaradiEvent evnt);
        Task UlozitPracoviste(DefinovanoPracovisteEvent evnt);
    }

    public class ExterniCiselnikyRepository : IExterniCiselnikyRepository
    {
        private IEventStore _store;
        private IEventSourcedSerializer _serializer;
        private string _prefix;

        public ExterniCiselnikyRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
        {
            _store = store;
            _prefix = prefix;
            _serializer = serializer;
        }

        public Task UlozitDodavatele(DefinovanDodavatelEvent evnt)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            return _store.AddToStream(_prefix + "-dodavatele", new[] { storedEvent }, EventStoreVersion.Any);
        }
        public Task UlozitVadu(DefinovanaVadaNaradiEvent evnt)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            return _store.AddToStream(_prefix + "-vady", new[] { storedEvent }, EventStoreVersion.Any);
        }
        public Task UlozitPracoviste(DefinovanoPracovisteEvent evnt)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            return _store.AddToStream(_prefix + "-pracoviste", new[] { storedEvent }, EventStoreVersion.Any);
        }
    }
}
