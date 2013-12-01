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
        UnikatnostNaradi Get();
        void Save(UnikatnostNaradi unikatnost);
    }

    public class UnikatnostNaradiRepositoryInMemory : EventSourcedRepository<UnikatnostNaradi>, IUnikatnostNaradiRepository
    {
        public UnikatnostNaradiRepositoryInMemory(IEventStore store, string prefix, IEventSourcedSerializer serializer)
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

        UnikatnostNaradi IUnikatnostNaradiRepository.Get()
        {
            return Get(Guid.Empty).GetAwaiter().GetResult();
        }

        void IUnikatnostNaradiRepository.Save(UnikatnostNaradi unikatnost)
        {
            Save(unikatnost).GetAwaiter().GetResult();
        }
    }
}
