using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain.UnikatnostNaradi;

namespace Vydejna.Domain.UnikatnostNaradi
{
    public class UnikatnostNaradiService
        : IProcess<DefinovatNaradiCommand>
        , IProcess<DokoncitDefiniciNaradiInternalCommand>
    {
        private IEventSourcedRepository<UnikatnostNaradiAggregate> _repoUnikatnost;

        public UnikatnostNaradiService(IUnikatnostNaradiRepository repoUnikatnost)
        {
            _repoUnikatnost = repoUnikatnost.AsGeneric();
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DefinovatNaradiCommand>(this);
            bus.Subscribe<DokoncitDefiniciNaradiInternalCommand>(this);
        }

        public Task Handle(DefinovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<UnikatnostNaradiAggregate>(_repoUnikatnost, UnikatnostNaradiId.Value)
                .OnRequest(unikatnost => unikatnost.ZahajitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }

        public Task Handle(DokoncitDefiniciNaradiInternalCommand msg)
        {
            return new EventSourcedServiceExecution<UnikatnostNaradiAggregate>(_repoUnikatnost, UnikatnostNaradiId.Value)
                .OnRequest(unikatnost => unikatnost.DokoncitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }
    }
}
