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
        Naradi Get(Guid id);
        void Save(Naradi naradi);
    }

    public class NaradiRepositoryInMemory : EventSourcedRepository<Naradi>, INaradiRepository
    {
        public NaradiRepositoryInMemory(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override Naradi CreateAggregate()
        {
            return new Naradi();
        }

        Naradi INaradiRepository.Get(Guid id)
        {
            return Get(id).GetAwaiter().GetResult();
        }

        void INaradiRepository.Save(Naradi naradi)
        {
            Save(naradi).GetAwaiter().GetResult();
        }
    }
}
