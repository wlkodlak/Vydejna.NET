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
        private Dictionary<IAggregateId, List<object>> _newEvents;
        private List<object> _allNewEvents;
        private bool _throwConcurrency;

        public TestRepository()
        {
            _allEvents = new Dictionary<IAggregateId, List<object>>();
            _newEvents = new Dictionary<IAggregateId, List<object>>();
            _allNewEvents = new List<object>();
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

        public IList<object> AllNewEvents()
        {
            return _allNewEvents;
        }

        public IList<object> NewEvents(IAggregateId id)
        {
            List<object> newEvents;
            if (_newEvents.TryGetValue(id, out newEvents))
                return newEvents;
            else
                return new object[0];
        }

        public IList<IAggregateId> SavedAggregateIds()
        {
            return _newEvents.Keys.ToList();
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

        public Task<bool> Save(T aggregate, IEventProcessTrackSource tracker)
        {
            try
            {
                if (_throwConcurrency)
                    return TaskUtils.FromResult(false);
                else
                {
                    var events = aggregate.GetChanges();
                    var id = aggregate.Id;

                    List<object> allEvents;
                    if (!_allEvents.TryGetValue(id, out allEvents))
                        _allEvents[id] = allEvents = new List<object>();
                    allEvents.AddRange(events);

                    List<object> newEvents;
                    if (!_newEvents.TryGetValue(id, out newEvents))
                        _newEvents[id] = newEvents = new List<object>();
                    newEvents.AddRange(events);
                    
                    _allNewEvents.AddRange(events);

                    aggregate.CommitChanges(allEvents.Count);
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
