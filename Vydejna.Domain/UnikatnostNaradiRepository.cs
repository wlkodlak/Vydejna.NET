using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IUnikatnostNaradiRepository
    {
        Task<UnikatnostNaradi> Get();
        Task Save(UnikatnostNaradi unikatnost);
    }

    public class UnikatnostNaradiRepository : EventSourcedRepository<UnikatnostNaradi>, IUnikatnostNaradiRepository
    {
        public UnikatnostNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override string StreamNameForId(Guid id)
        {
            return Prefix;
        }

        protected override UnikatnostNaradi CreateAggregate()
        {
            return new UnikatnostNaradi();
        }

        Task<UnikatnostNaradi> IUnikatnostNaradiRepository.Get()
        {
            return Get(Guid.Empty);
        }

        Task IUnikatnostNaradiRepository.Save(UnikatnostNaradi unikatnost)
        {
            return Save(unikatnost);
        }
    }
}
