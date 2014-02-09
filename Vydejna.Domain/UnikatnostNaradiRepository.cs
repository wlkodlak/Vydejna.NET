using ServiceLib;
using System;

namespace Vydejna.Domain
{
    public interface IUnikatnostNaradiRepository
    {
        void Load(Action<UnikatnostNaradi> onLoaded, Action<Exception> onError);
        void Save(UnikatnostNaradi aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError);
    }

    public class UnikatnostNaradiId : IAggregateId
    {
        public override int GetHashCode()
        {
            return 498273;
        }
        public override bool Equals(object obj)
        {
            return obj is UnikatnostNaradiId;
        }
        public override string ToString()
        {
            return "UnikatnostNaradiId";
        }
        private static readonly UnikatnostNaradiId _value = new UnikatnostNaradiId();
        public static UnikatnostNaradiId Value { get { return _value; } }
    }

    public class UnikatnostNaradiRepository : EventSourcedRepository<UnikatnostNaradi>, IUnikatnostNaradiRepository
    {
        public UnikatnostNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override string StreamNameForId(IAggregateId id)
        {
            return Prefix;
        }

        protected override UnikatnostNaradi CreateAggregate()
        {
            return new UnikatnostNaradi();
        }

        public void Load(Action<UnikatnostNaradi> onLoaded, Action<Exception> onError)
        {
            Load(UnikatnostNaradiId.Value, onLoaded, () => onLoaded(new UnikatnostNaradi()), onError);
        }
    }
}
