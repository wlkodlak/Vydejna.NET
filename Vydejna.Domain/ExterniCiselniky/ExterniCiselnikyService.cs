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

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<CommandExecution<DefinovanDodavatelEvent>>(this);
            bus.Subscribe<CommandExecution<DefinovanaVadaNaradiEvent>>(this);
            bus.Subscribe<CommandExecution<DefinovanoPracovisteEvent>>(this);
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
        private string _prefix;

        public ExterniCiselnikyRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
        {
            _store = store;
            _prefix = prefix;
            _serializer = serializer;
        }

        public void UlozitDodavatele(DefinovanDodavatelEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream(_prefix + "-dodavatele", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
        public void UlozitVadu(DefinovanaVadaNaradiEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream(_prefix + "-vady", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
        public void UlozitPracoviste(DefinovanoPracovisteEvent evnt, Action onComplete, Action<Exception> onError)
        {
            var storedEvent = new EventStoreEvent();
            _serializer.Serialize(evnt, storedEvent);
            _store.AddToStream(_prefix + "-pracoviste", new[] { storedEvent }, EventStoreVersion.Any, onComplete, () => onError(new InvalidOperationException("Unexpected concurrency exception")), onError);
        }
    }
}
