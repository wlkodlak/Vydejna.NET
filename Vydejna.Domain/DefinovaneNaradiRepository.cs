using ServiceLib;

namespace Vydejna.Domain
{
    public interface IDefinovaneNaradiRepository : IEventSourcedRepository<DefinovaneNaradi>
    {
    }

    public class DefinovaneNaradiRepository : EventSourcedRepository<DefinovaneNaradi>, IDefinovaneNaradiRepository
    {
        public DefinovaneNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override DefinovaneNaradi CreateAggregate()
        {
            return new DefinovaneNaradi();
        }
    }
}
