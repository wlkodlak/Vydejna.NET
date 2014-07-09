using log4net;
using System;
using System.Collections.Generic;
using ServiceLib;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Domain.ExterniCiselniky
{
    public class ExterniCiselnikyService
        : IProcessCommand<DefinovanDodavatelEvent>
        , IProcessCommand<DefinovanaVadaNaradiEvent>
        , IProcessCommand<DefinovanoPracovisteEvent>
    {
        private readonly IExternalEventRepository _repository;
        private readonly IEventProcessTrackCoordinator _tracking;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.ExterniCiselniky");

        public ExterniCiselnikyService(IExternalEventRepository repository, IEventProcessTrackCoordinator tracking)
        {
            _repository = repository;
            _tracking = tracking;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DefinovanDodavatelEvent>(this);
            bus.Subscribe<DefinovanaVadaNaradiEvent>(this);
            bus.Subscribe<DefinovanoPracovisteEvent>(this);
        }

        public Task<CommandResult> Handle(DefinovanDodavatelEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, _tracking, "dodavatele").AddEvent(msg).Execute();
        }
        public Task<CommandResult> Handle(DefinovanaVadaNaradiEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, _tracking, "vady").AddEvent(msg).Execute();
        }
        public Task<CommandResult> Handle(DefinovanoPracovisteEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, _tracking, "pracoviste").AddEvent(msg).Execute();
        }
    }
}
