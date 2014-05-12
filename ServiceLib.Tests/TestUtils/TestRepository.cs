using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestRepository<T> : IEventSourcedRepository<T>
        where T : class, IEventSourcedAggregate, new()
    {
        private Dictionary<IAggregateId, List<object>> _allEvents;
        private List<object> _newEvents;
        private bool _throwConcurrency;

        public TestRepository()
        {
            _allEvents = new Dictionary<IAggregateId, List<object>>();
            _newEvents = new List<object>();
        }

        public void AddEvents(IAggregateId id, params object[] events)
        {
            List<object> all;
            if (!_allEvents.TryGetValue(id, out all))
                _allEvents[id] = all = new List<object>();
            all.AddRange(events);
        }

        public void ThrowConcurrency()
        {
            _throwConcurrency = true;
        }

        public IList<object> NewEvents()
        {
            return _newEvents;
        }

        public Task<T> Load(IAggregateId id)
        {
            List<object> all;
            if (!_allEvents.TryGetValue(id, out all))
                return TaskUtils.FromResult<T>(null);
            else
            {
                var aggregate = new T();
                aggregate.LoadFromEvents(all);
                aggregate.CommitChanges(all.Count);
                return TaskUtils.FromResult(aggregate);
            }
        }

        public Task<bool> Save(T aggregate)
        {
            try
            {
                if (_throwConcurrency)
                    return TaskUtils.FromResult(false);
                else
                {
                    var events = aggregate.GetChanges();
                    var id = aggregate.Id;

                    List<object> all;
                    if (!_allEvents.TryGetValue(id, out all))
                        _allEvents[id] = all = new List<object>();
                    all.AddRange(events);
                    _newEvents.AddRange(events);

                    aggregate.CommitChanges(all.Count);
                    return TaskUtils.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<bool>(ex);
            }
        }
    }
}
