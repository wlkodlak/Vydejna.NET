using ServiceLib;

namespace Vydejna.Domain.DefinovaneNaradi
{
    public interface IDefinovaneNaradiRepository : IEventSourcedRepository<DefinovaneNaradiAggregate>
    {
    }

    public class DefinovaneNaradiRepository : EventSourcedRepository<DefinovaneNaradiAggregate>, IDefinovaneNaradiRepository
    {
        public DefinovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override DefinovaneNaradiAggregate CreateAggregate()
        {
            return new DefinovaneNaradiAggregate();
        }
    }
}
