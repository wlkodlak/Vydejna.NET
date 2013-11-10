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

    public class UnikatnostNaradiRepositoryInMemory : IUnikatnostNaradiRepository
    {
        private IBus  _bus;
        private List<object> _data;

        public UnikatnostNaradiRepositoryInMemory(IBus bus)
        {
            _bus = bus;
            _data = new List<object>();
        }

        public UnikatnostNaradi Get()
        {
            return UnikatnostNaradi.LoadFrom(_data);
        }

        public void Save(UnikatnostNaradi unikatnost)
        {
            var newEvents = unikatnost.GetChanges();
            _data.AddRange(newEvents);
            _bus.Publish(newEvents);
        }

        public void Clear()
        {
            _data.Clear();
        }

        public void AddData(IList<object> newEvents)
        {
            _data.AddRange(newEvents);
        }

        public IList<object> GetData()
        {
            return _data;
        }
    }
}
