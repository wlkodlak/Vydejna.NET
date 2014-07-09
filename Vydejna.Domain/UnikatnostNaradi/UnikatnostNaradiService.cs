using log4net;
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
        : IProcessCommand<DefinovatNaradiCommand>
        , IProcessCommand<DokoncitDefiniciNaradiInternalCommand>
    {
        private readonly IEventSourcedRepository<UnikatnostNaradiAggregate> _repoUnikatnost;
        private readonly IEventProcessTrackCoordinator _tracking;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.UnikatnostNaradi");

        public UnikatnostNaradiService(IUnikatnostNaradiRepository repoUnikatnost, IEventProcessTrackCoordinator tracking)
        {
            _repoUnikatnost = repoUnikatnost.AsGeneric();
            _tracking = tracking;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DefinovatNaradiCommand>(this);
            bus.Subscribe<DokoncitDefiniciNaradiInternalCommand>(this);
        }

        public Task<CommandResult> Handle(DefinovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<UnikatnostNaradiAggregate>(_repoUnikatnost, UnikatnostNaradiId.Value, Logger, _tracking)
                .OnRequest(unikatnost => unikatnost.ZahajitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }

        public Task<CommandResult> Handle(DokoncitDefiniciNaradiInternalCommand msg)
        {
            return new EventSourcedServiceExecution<UnikatnostNaradiAggregate>(_repoUnikatnost, UnikatnostNaradiId.Value, Logger, _tracking)
                .OnRequest(unikatnost => unikatnost.DokoncitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }
    }
}
