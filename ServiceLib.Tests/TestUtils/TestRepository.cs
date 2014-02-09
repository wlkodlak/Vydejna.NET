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

        public void Load(IAggregateId id, Action<T> onLoaded, Action onMissing, Action<Exception> onError)
        {
            List<object> all;
            if (!_allEvents.TryGetValue(id, out all))
                onMissing();
            else
            {
                var aggregate = new T();
                aggregate.LoadFromEvents(all);
                aggregate.CommitChanges(all.Count);
                onLoaded(aggregate);
            }
        }

        public void Save(T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError)
        {
            try
            {
                if (_throwConcurrency)
                    onConcurrency();
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
                }
            }
            catch (Exception ex)
            {
                onError(ex);
                return;
            }
            onSaved();
        }
    }
}
