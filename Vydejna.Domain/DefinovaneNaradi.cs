using ServiceLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class DefinovaneNaradi : EventSourcedGuidAggregate
    {
        private bool _aktivni;

        public DefinovaneNaradi()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public void Aktivovat()
        {
            if (!_aktivni)
                ApplyChange(new AktivovanoNaradiEvent { NaradiId = GetGuid() });
        }

        public void Deaktivovat()
        {
            if (_aktivni)
                ApplyChange(new DeaktivovanoNaradiEvent { NaradiId = GetGuid() });
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

        public static DefinovaneNaradi Definovat(Guid id, string vykres, string rozmer, string druh)
        {
            var naradi = new DefinovaneNaradi();
            naradi.ApplyChange(new DefinovanoNaradiEvent
            {
                NaradiId = id,
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh
            });
            return naradi;
        }

        public static DefinovaneNaradi LoadFrom(IList<object> udalosti)
        {
            var naradi = new DefinovaneNaradi();
            var aggregate = naradi as IEventSourcedAggregate;
            aggregate.LoadFromEvents(udalosti);
            return naradi;
        }
    }
}
