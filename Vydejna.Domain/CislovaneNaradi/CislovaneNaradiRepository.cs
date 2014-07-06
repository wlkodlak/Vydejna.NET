using ServiceLib;

namespace Vydejna.Domain.CislovaneNaradi
{
    public class CislovaneNaradiRepository : EventSourcedRepository<CislovaneNaradiAggregate>
    {
        public CislovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
            SnapshotInterval = 20;
        }

        protected override CislovaneNaradiAggregate CreateAggregate()
        {
            return new CislovaneNaradiAggregate();
        }
    }
}
