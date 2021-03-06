﻿using ServiceLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain.UnikatnostNaradi
{
    public class UnikatnostNaradiAggregate : EventSourcedAggregate
    {
        private Dictionary<Klic, StavNaradi> _existujici = new Dictionary<Klic, StavNaradi>();

        public UnikatnostNaradiAggregate()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public override IAggregateId Id
        {
            get { return UnikatnostNaradiId.Value; }
        }

        private struct Klic { public string Vykres, Rozmer; };
        private class StavNaradi { public Guid Id; public bool DokoncenaDefinice; public string Vykres, Rozmer; }

        public static UnikatnostNaradiAggregate LoadFrom(List<object> udalosti)
        {
            var unikatnost = new UnikatnostNaradiAggregate();
            var aggregate = unikatnost as IEventSourcedAggregate;
            aggregate.LoadFromEvents(udalosti);
            return unikatnost;
        }

        public void ZahajitDefinici(Guid idNaradi, string vykres, string rozmer, string druh)
        {
            var klic = new Klic() { Vykres = vykres, Rozmer = rozmer };
            StavNaradi stav;
            if (!_existujici.TryGetValue(klic, out stav))
            {
                if (idNaradi == Guid.Empty)
                    idNaradi = Guid.NewGuid();
                ApplyChange(new ZahajenaDefiniceNaradiEvent { NaradiId = idNaradi, Vykres = vykres, Rozmer = rozmer, Druh = druh, Verze = CurrentVersion + 1 });
            }
            else if (stav.DokoncenaDefinice)
                ApplyChange(new ZahajenaAktivaceNaradiEvent { NaradiId = stav.Id, Verze = CurrentVersion + 1 });
        }

        public void DokoncitDefinici(Guid idNaradi, string vykres, string rozmer, string druh)
        {
            ApplyChange(new DokoncenaDefiniceNaradiEvent { NaradiId = idNaradi, Vykres = vykres, Rozmer = rozmer, Druh = druh, Verze = CurrentVersion + 1 });
        }

        private void ApplyChange(ZahajenaDefiniceNaradiEvent evt)
        {
            RecordChange(evt);
            var klic = new Klic() { Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            var stav = new StavNaradi { Id = evt.NaradiId, DokoncenaDefinice = false, Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            _existujici[klic] = stav;
        }

        private void ApplyChange(ZahajenaAktivaceNaradiEvent evt)
        {
            RecordChange(evt);
        }

        private void ApplyChange(DokoncenaDefiniceNaradiEvent evt)
        {
            RecordChange(evt);
            var klic = new Klic() { Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            _existujici[klic].DokoncenaDefinice = true;
        }
    }
}
