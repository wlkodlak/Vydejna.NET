using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface INaradiRepository
    {
        Task<Naradi> Get(Guid id);
        Task Save(Naradi naradi);
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

        Task<Naradi> INaradiRepository.Get(Guid id)
        {
            return Get(id);
        }

        Task INaradiRepository.Save(Naradi naradi)
        {
            return Save(naradi);
        }
    }
}
