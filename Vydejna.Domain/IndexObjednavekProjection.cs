using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using ServiceStack.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class IndexObjednavekProjection
        : IEventProjection
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
    {
    }

    public class IndexObjednavekSerializer
    {

    }



    public class IndexObjednavekReader
        : IAnswer<NajitObjednavkuRequest, NajitObjednavkuResponse>
        , IAnswer<NajitDodaciListRequest, NajitDodaciListResponse>
    {

    }
}
