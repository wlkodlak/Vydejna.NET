using ServiceLib;
using System;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.DefinovaneNaradi
{
    public class DefinovaneNaradiService
        : IProcess<AktivovatNaradiCommand>
        , IProcess<DeaktivovatNaradiCommand>
        , IProcess<DefinovatNaradiInternalCommand>
    {
        private log4net.ILog _log;
        private IDefinovaneNaradiRepository _repoNaradi;

        public DefinovaneNaradiService(IDefinovaneNaradiRepository repoNaradi)
        {
            _log = log4net.LogManager.GetLogger(typeof(DefinovaneNaradiService));
            _repoNaradi = repoNaradi;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<AktivovatNaradiCommand>(this);
            bus.Subscribe<DeaktivovatNaradiCommand>(this);
            bus.Subscribe<DefinovatNaradiInternalCommand>(this);
        }

        public Task Handle(AktivovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId())
                .OnRequest(naradi => naradi.Aktivovat()).Execute();
        }
        public Task Handle(DeaktivovatNaradiCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId())
                .OnRequest(naradi => naradi.Deaktivovat()).Execute();
        }

        public Task Handle(DefinovatNaradiInternalCommand msg)
        {
            return new EventSourcedServiceExecution<DefinovaneNaradiAggregate>(_repoNaradi, msg.NaradiId.ToId())
                .OnNew(naradi => DefinovaneNaradiAggregate.Definovat(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }
    }
}
