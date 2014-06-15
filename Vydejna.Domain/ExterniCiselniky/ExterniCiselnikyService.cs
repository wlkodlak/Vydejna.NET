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
        private IExternalEventRepository _repository;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.ExterniCiselniky");

        public ExterniCiselnikyService(IExternalEventRepository repository)
        {
            _repository = repository;
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DefinovanDodavatelEvent>(this);
            bus.Subscribe<DefinovanaVadaNaradiEvent>(this);
            bus.Subscribe<DefinovanoPracovisteEvent>(this);
        }

        public Task<CommandResult> Handle(DefinovanDodavatelEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, "dodavatele").AddEvent(msg).Execute();
        }
        public Task<CommandResult> Handle(DefinovanaVadaNaradiEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, "vady").AddEvent(msg).Execute();
        }
        public Task<CommandResult> Handle(DefinovanoPracovisteEvent msg)
        {
            return new ExternalEventServiceExecution(_repository, Logger, "pracoviste").AddEvent(msg).Execute();
        }
    }
}
