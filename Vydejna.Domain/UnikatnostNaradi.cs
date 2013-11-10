using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class UnikatnostNaradi
    {
        private List<object> _changes = new List<object>();
        private Dictionary<Klic, StavNaradi> _existujici = new Dictionary<Klic, StavNaradi>();

        private struct Klic { public string Vykres, Rozmer; };
        private class StavNaradi { public Guid Id; public bool DokoncenaDefinice; public string Vykres, Rozmer; }

        protected void AddToHistory(object ev, bool fromHistory)
        {
            if (!fromHistory)
                _changes.Add(ev);
        }

        public static UnikatnostNaradi LoadFrom(List<object> udalosti)
        {
            var unikatnost = new UnikatnostNaradi();
            foreach (var ev in udalosti)
                unikatnost.ApplyChange((dynamic)ev, true);
            return unikatnost;
        }

        public IList<object> GetChanges()
        {
            return _changes;
        }

        public void ZahajitDefinici(Guid idNaradi, string vykres, string rozmer, string druh)
        {
            var klic = new Klic() { Vykres = vykres, Rozmer = rozmer };
            StavNaradi stav;
            if (!_existujici.TryGetValue(klic, out stav))
            {
                if (idNaradi == Guid.Empty)
                    idNaradi = Guid.NewGuid();
                ApplyChange(new ZahajenaDefiniceNaradiEvent { NaradiId = idNaradi, Vykres = vykres, Rozmer = rozmer, Druh = druh });
            }
            else if (stav.DokoncenaDefinice)
                ApplyChange(new ZahajenaAktivaceNaradiEvent { NaradiId = stav.Id });
        }

        public void DokoncitDefinici(Guid idNaradi, string vykres, string rozmer, string druh)
        {
            ApplyChange(new DokoncenaDefiniceNaradiEvent { NaradiId = idNaradi, Vykres = vykres, Rozmer = rozmer, Druh = druh });
        }

        private void ApplyChange(ZahajenaDefiniceNaradiEvent evt, bool history = false)
        {
            AddToHistory(evt, history);
            var klic = new Klic() { Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            var stav = new StavNaradi { Id = evt.NaradiId, DokoncenaDefinice = false, Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            _existujici[klic] = stav;
        }

        private void ApplyChange(ZahajenaAktivaceNaradiEvent evt, bool history = false)
        {
            AddToHistory(evt, history);
        }

        private void ApplyChange(DokoncenaDefiniceNaradiEvent evt, bool history = false)
        {
            AddToHistory(evt, history);
            var klic = new Klic() { Vykres = evt.Vykres, Rozmer = evt.Rozmer };
            _existujici[klic].DokoncenaDefinice = true;
        }

    }
}
