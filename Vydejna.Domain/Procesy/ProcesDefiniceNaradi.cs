using ServiceLib;
using System;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.Procesy
{
    public class ProcesDefiniceNaradi
        : ISubscribeToCommandManager
        , IProcess<ZahajenaDefiniceNaradiEvent>
        , IProcess<ZahajenaAktivaceNaradiEvent>
        , IProcess<DefinovanoNaradiEvent>
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

        public Task Handle(ZahajenaDefiniceNaradiEvent evt)
        {
            return _bus.SendCommand(new DefinovatNaradiInternalCommand
            {
                NaradiId = evt.NaradiId,
                Vykres = evt.Vykres,
                Rozmer = evt.Rozmer,
                Druh = evt.Druh
            });
        }

        public Task Handle(ZahajenaAktivaceNaradiEvent evt)
        {
            return _bus.SendCommand(new AktivovatNaradiCommand
            {
                NaradiId = evt.NaradiId
            });
        }

        public Task Handle(DefinovanoNaradiEvent evt)
        {
            return _bus.SendCommand(new DokoncitDefiniciNaradiInternalCommand
            {
                NaradiId = evt.NaradiId,
                Vykres = evt.Vykres,
                Rozmer = evt.Rozmer,
                Druh = evt.Druh
            });
        }
    }
}
