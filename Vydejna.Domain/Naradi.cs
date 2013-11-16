using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class Naradi : EventSourcedAggregate
    {
        private bool _aktivni;

        public void Aktivovat()
        {
            if (!_aktivni)
                ApplyChange(new AktivovanoNaradiEvent { NaradiId = Id });
        }

        public void Deaktivovat()
        {
            if (_aktivni)
                ApplyChange(new DeaktivovanoNaradiEvent { NaradiId = Id });
        }

        private void ApplyChange(DefinovanoNaradiEvent ev)
        {
            RecordChange(ev);
            Id = ev.NaradiId;
            _aktivni = true;
        }

        private void ApplyChange(AktivovanoNaradiEvent ev)
        {
            RecordChange(ev);
            _aktivni = true;
        }

        private void ApplyChange(DeaktivovanoNaradiEvent ev)
        {
            RecordChange(ev);
            _aktivni = false;
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

        public static Naradi LoadFrom(IList<object> udalosti)
        {
            var naradi = new Naradi();
            var aggregate = naradi as IEventSourcedAggregate;
            aggregate.LoadFromEvents(udalosti);
            return naradi;
        }

        protected override void DispatchEvent(object evt)
        {
            ApplyChange((dynamic)evt);
        }
    }
}
