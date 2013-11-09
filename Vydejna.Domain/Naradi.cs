using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class Naradi
    {
        private Guid _id;

        private List<object> _changes = new List<object>();

        protected void AddToHistory(object ev, bool fromHistory)
        {
            if (!fromHistory)
                _changes.Add(ev);
        }

        public void Aktivovat()
        {
            ApplyChange(new AktivovanoNaradiEvent { NaradiId = _id });
        }

        public void Deaktivovat()
        {
            ApplyChange(new DeaktivovanoNaradiEvent { NaradiId = _id });
        }

        private void ApplyChange(DefinovanoNaradiEvent ev, bool fromHistory = false)
        {
            AddToHistory(ev, fromHistory);
            _id = ev.NaradiId;
        }

        private void ApplyChange(AktivovanoNaradiEvent ev, bool fromHistory = false)
        {
            AddToHistory(ev, fromHistory);
        }

        private void ApplyChange(DeaktivovanoNaradiEvent ev, bool fromHistory = false)
        {
            AddToHistory(ev, fromHistory);
        }

        public static Naradi Definovat(Guid id, string vykres, string rozmer, string druh)
        {
            var naradi = new Naradi();
            naradi.ApplyChange(new DefinovanoNaradiEvent
            {
                NaradiId = id,
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh
            });
            return naradi;
        }

        public static Naradi LoadFrom(List<object> udalosti)
        {
            var naradi = new Naradi();
            foreach (var ev in udalosti)
                naradi.ApplyChange((dynamic)ev, true);
            return naradi;
        }

        public IList<object> GetChanges()
        {
            return _changes;
        }
    }
}
