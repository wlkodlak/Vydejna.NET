using ServiceLib;
using System;
using System.Threading.Tasks;

namespace Vydejna.Domain.UnikatnostNaradi
{
    public interface IUnikatnostNaradiRepository
    {
        Task<UnikatnostNaradiAggregate> Load();
        Task<bool> Save(UnikatnostNaradiAggregate aggregate, IEventProcessTrackSource tracker);
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

    public class UnikatnostNaradiRepository : EventSourcedRepository<UnikatnostNaradiAggregate>, IUnikatnostNaradiRepository
    {
        public UnikatnostNaradiRepository(IEventStore store, string prefix, IEventSourcedSerializer serializer)
            : base(store, prefix, serializer)
        {
        }

        protected override string StreamNameForId(IAggregateId id)
        {
            return Prefix;
        }

        protected override UnikatnostNaradiAggregate CreateAggregate()
        {
            return new UnikatnostNaradiAggregate();
        }

        public Task<UnikatnostNaradiAggregate> Load()
        {
            return Load(UnikatnostNaradiId.Value);
        }
    }

    public static class UnikatnostNaradiRepositoryExtensions
    {
        public static IEventSourcedRepository<UnikatnostNaradiAggregate> AsGeneric(this IUnikatnostNaradiRepository repository)
        {
            var direct = repository as IEventSourcedRepository<UnikatnostNaradiAggregate>;
            if (direct != null)
                return direct;
            else
                return new UnikatnostNaradiRepositoryToGeneric(repository);
        }

        private class UnikatnostNaradiRepositoryToGeneric : IEventSourcedRepository<UnikatnostNaradiAggregate>
        {
            private IUnikatnostNaradiRepository _repository;

            public UnikatnostNaradiRepositoryToGeneric(IUnikatnostNaradiRepository repository)
            {
                _repository = repository;
            }

            public Task<UnikatnostNaradiAggregate> Load(IAggregateId id)
            {
                return _repository.Load();
            }

            public Task<bool> Save(UnikatnostNaradiAggregate aggregate, IEventProcessTrackSource tracker)
            {
                return _repository.Save(aggregate, tracker);
            }
        }
    }
}
