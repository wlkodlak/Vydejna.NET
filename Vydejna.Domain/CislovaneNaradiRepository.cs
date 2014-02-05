using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;

namespace Vydejna.Domain
{
    public class CislovaneNaradiRepository : EventSourcedRepository<CislovaneNaradi>
    {
        public CislovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override CislovaneNaradi CreateAggregate()
        {
            return new CislovaneNaradi();
        }
    }
}
