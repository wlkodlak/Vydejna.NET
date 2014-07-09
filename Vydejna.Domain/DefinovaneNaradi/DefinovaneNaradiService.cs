using log4net;
using ServiceLib;
using System;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.DefinovaneNaradi
{
    public class DefinovaneNaradiService
        : IProcessCommand<AktivovatNaradiCommand>
        , IProcessCommand<DeaktivovatNaradiCommand>
        , IProcessCommand<DefinovatNaradiInternalCommand>
    {
        private readonly IDefinovaneNaradiRepository _repoNaradi;
        private readonly IEventProcessTrackCoordinator _tracking;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.DefinovaneNaradi");

        public DefinovaneNaradiService(IDefinovaneNaradiRepository repoNaradi, IEventProcessTrackCoordinator tracking)
        {
            _repoNaradi = repoNaradi;
            _tracking = tracking;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<AktivovatNaradiCommand>(this);
            bus.Subscribe<DeaktivovatNaradiCommand>(this);
            bus.Subscribe<DefinovatNaradiInternalCommand>(this);
        }

        public Task<CommandResult> Handle(AktivovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId(), Logger, _tracking)
                .OnExisting(naradi => naradi.Aktivovat())
                .Execute();
        }
        public Task<CommandResult> Handle(DeaktivovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId(), Logger, _tracking)
                .OnExisting(naradi => naradi.Deaktivovat())
                .Execute();
        }

        public Task<CommandResult> Handle(DefinovatNaradiInternalCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId(), Logger, _tracking)
                .OnNew(() => DefinovaneNaradiAggregate.Definovat(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }
    }
}
