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
        private static readonly Guid _id = new Guid("341FA50E-E1E9-4C9B-AC89-AFED9ADDA843");

        public UnikatnostNaradiRepositoryInMemory(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override UnikatnostNaradi CreateAggregate()
        {
            return new UnikatnostNaradi();
        }

        UnikatnostNaradi IUnikatnostNaradiRepository.Get()
        {
            return Get(_id).GetAwaiter().GetResult();
        }

        void IUnikatnostNaradiRepository.Save(UnikatnostNaradi unikatnost)
        {
            Save(unikatnost).GetAwaiter().GetResult();
        }
    }
}
