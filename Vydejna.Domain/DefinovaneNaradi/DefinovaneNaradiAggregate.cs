using ServiceLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain.DefinovaneNaradi
{
    public class DefinovaneNaradiAggregate : EventSourcedGuidAggregate
    {
        private bool _aktivni;

        public DefinovaneNaradiAggregate()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public void Aktivovat()
        {
            if (!_aktivni)
                ApplyChange(new AktivovanoNaradiEvent { NaradiId = GetGuid(), Verze = CurrentVersion + 1 });
        }

        public void Deaktivovat()
        {
            if (_aktivni)
                ApplyChange(new DeaktivovanoNaradiEvent { NaradiId = GetGuid(), Verze = CurrentVersion + 1 });
        }

        private void ApplyChange(DefinovanoNaradiEvent ev)
        {
            RecordChange(ev);
            SetGuid(ev.NaradiId);
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

        public static DefinovaneNaradiAggregate Definovat(Guid id, string vykres, string rozmer, string druh)
        {
            var naradi = new DefinovaneNaradiAggregate();
            naradi.ApplyChange(new DefinovanoNaradiEvent
            {
                NaradiId = id,
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh,
                Verze = 1
            });
            return naradi;
        }

        public static DefinovaneNaradiAggregate LoadFrom(IList<object> udalosti)
        {
            var naradi = new DefinovaneNaradiAggregate();
            var aggregate = naradi as IEventSourcedAggregate;
            aggregate.LoadFromEvents(udalosti);
            return naradi;
        }
    }
}
