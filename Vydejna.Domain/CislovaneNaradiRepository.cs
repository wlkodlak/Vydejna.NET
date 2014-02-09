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
