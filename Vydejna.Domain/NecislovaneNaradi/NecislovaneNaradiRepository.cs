using ServiceLib;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class NecislovaneNaradiRepository : EventSourcedRepository<NecislovaneNaradiAggregate>
    {
        public NecislovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override NecislovaneNaradiAggregate CreateAggregate()
        {
            return new NecislovaneNaradiAggregate();
        }
    }
}
