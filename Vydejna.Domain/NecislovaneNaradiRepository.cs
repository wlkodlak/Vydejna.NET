using ServiceLib;

namespace Vydejna.Domain
{
    public class NecislovaneNaradiRepository : EventSourcedRepository<NecislovaneNaradi>
    {
        public NecislovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override NecislovaneNaradi CreateAggregate()
        {
            return new NecislovaneNaradi();
        }
    }
}
