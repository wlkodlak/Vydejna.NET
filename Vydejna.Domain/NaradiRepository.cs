using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface INaradiRepository : IEventSourcedRepository<Naradi>
    {
    }

    public class NaradiRepository : EventSourcedRepository<Naradi>, INaradiRepository
    {
        public NaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override Naradi CreateAggregate()
        {
            return new Naradi();
        }
    }
}
