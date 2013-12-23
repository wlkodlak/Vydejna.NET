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
        void Load(Action<UnikatnostNaradi> onLoaded, Action<Exception> onError);
        void Save(UnikatnostNaradi aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError);
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

        public void Load(Action<UnikatnostNaradi> onLoaded, Action<Exception> onError)
        {
            Load(Guid.Empty, onLoaded, () => onLoaded(new UnikatnostNaradi()), onError);
        }
    }
}
