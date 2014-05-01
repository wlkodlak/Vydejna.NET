using System;
using System.Collections.Generic;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain.ExterniCiselniky
{
    public class ExterniCiselnikyService
        : IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<DefinovanaVadaNaradiEvent>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
    {
        private IExterniCiselnikyRepository _repository;

        public ExterniCiselnikyService(IExterniCiselnikyRepository repository)
        {
            _repository = repository;
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> msg)
        {
            _repository.UlozitDodavatele(msg.Command, msg.OnCompleted, msg.OnError);
        }
        public void Handle(CommandExecution<DefinovanaVadaNaradiEvent> msg)
        {
            _repository.UlozitVadu(msg.Command, msg.OnCompleted, msg.OnError);
        }
        public void Handle(CommandExecution<DefinovanoPracovisteEvent> msg)
        {
            _repository.UlozitPracoviste(msg.Command, msg.OnCompleted, msg.OnError);
        }
    }

    public interface IExterniCiselnikyRepository
    {
        void UlozitDodavatele(DefinovanDodavatelEvent evnt, Action onComplete, Action<Exception> onError);
        void UlozitVadu(DefinovanaVadaNaradiEvent evnt, Action onComplete, Action<Exception> onError);
        void UlozitPracoviste(DefinovanoPracovisteEvent evnt, Action onComplete, Action<Exception> onError);
    }

    public class ExterniCiselnikyRepository : IExterniCiselnikyRepository
    {
        private IEventStore _store;
        private IEventSourcedSerializer _serializer;

        public ExterniCiselnikyRepository(IEventStore store, IEventSourcedSerializer serializer)
        {
            _store = store;
            _serializer = serializer;
        }

        public void UlozitDodavatele(DefinovanDodavatelEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream("dodavatele", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
        public void UlozitVadu(DefinovanaVadaNaradiEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream("vady", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
        public void UlozitPracoviste(DefinovanoPracovisteEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream("pracoviste", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
    }
}
