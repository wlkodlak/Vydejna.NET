using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Procesy
{
    public class ProcesDefiniceNaradi
        : ISubscribeToCommandManager
        , IHandle<CommandExecution<ZahajenaDefiniceNaradiEvent>>
        , IHandle<CommandExecution<ZahajenaAktivaceNaradiEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
    {
        private IPublisher _bus;

        public ProcesDefiniceNaradi(IPublisher bus)
        {
            _bus = bus;
        }

        public void Subscribe(ICommandSubscriptionManager subscriptions)
        {
            subscriptions.Register<ZahajenaDefiniceNaradiEvent>(this);
            subscriptions.Register<ZahajenaAktivaceNaradiEvent>(this);
            subscriptions.Register<DefinovanoNaradiEvent>(this);
        }

        private void Handle<T>(T cmd, Action onCompleted, Action<Exception> onError)
        {
            _bus.Publish(new CommandExecution<T>(cmd, onCompleted, onError));
        }

        public void Handle(CommandExecution<ZahajenaDefiniceNaradiEvent> evt)
        {
            Handle(new DefinovatNaradiCommand
            {
                NaradiId = evt.Command.NaradiId,
                Vykres = evt.Command.Vykres,
                Rozmer = evt.Command.Rozmer,
                Druh = evt.Command.Druh
            }, evt.OnCompleted, evt.OnError);
        }

        public void Handle(CommandExecution<ZahajenaAktivaceNaradiEvent> evt)
        {
            Handle(new AktivovatNaradiCommand
            {
                NaradiId = evt.Command.NaradiId
            }, evt.OnCompleted, evt.OnError);
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> evt)
        {
            Handle(new DokoncitDefiniciNaradiInternalCommand
            {
                NaradiId = evt.Command.NaradiId,
                Vykres = evt.Command.Vykres,
                Rozmer = evt.Command.Rozmer,
                Druh = evt.Command.Druh
            }, evt.OnCompleted, evt.OnError);
        }
    }
}
