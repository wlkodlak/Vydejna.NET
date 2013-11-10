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

    public class NaradiRepositoryInMemory : INaradiRepository
    {
        private Dictionary<Guid, List<object>> _data = new Dictionary<Guid, List<object>>();
        private IBus _bus;

        public NaradiRepositoryInMemory(IBus bus)
        {
            _bus = bus;
        }

        public Naradi Get(Guid id)
        {
            var events = GetDataFor(id);
            if (events != null)
                return Naradi.LoadFrom(events);
            else
                return null;
        }

        public void Save(Naradi naradi)
        {
            var id = naradi.Id;
            var newEvents = naradi.GetChanges();
            AddData(id, newEvents);
            _bus.Publish(newEvents);
        }

        public void Clear()
        {
            _data.Clear();
        }

        public void AddData(Guid id, IList<object> newEvents)
        {
            List<object> events;
            if (!_data.TryGetValue(id, out events))
                _data[id] = events = new List<object>();
            events.AddRange(newEvents);
        }

        public IList<object> GetDataFor(Guid id)
        {
            List<object> events;
            if (!_data.TryGetValue(id, out events))
                return null;
            else if (events.Count == 0)
                return null;
            else
                return events;
        }

        public IList<Guid> GetGuids()
        {
            return _data.Keys.ToList();
        }
    }
}
