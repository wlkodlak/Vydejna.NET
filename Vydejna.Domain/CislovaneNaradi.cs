using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class CislovaneNaradi : EventSourcedAggregate
    {
        private Guid _naradiId;
        private int _cisloNaradi;
        private UmisteniNaradi _umisteni;

        private void ApplyChange(CislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            RecordChange(evnt);
            _naradiId = evnt.NaradiId;
            _cisloNaradi = evnt.CisloNaradi;
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }

        private void ApplyChange(NastalaPotrebaUpravitStavNaSkladeEvent evnt)
        {
            RecordChange(evnt);
        }

        protected override void DispatchEvent(object evt)
        {
            ApplyChange((dynamic)evt);
        }

        public void Execute(CislovaneNaradiPrijmoutNaVydejnuCommand cmd, ITime time)
        {
            if (_cisloNaradi != 0)
                throw new InvalidOperationException("Naradi s timto cislem jiz existuje");
            if (cmd.CisloNaradi <= 0)
                throw new ArgumentOutOfRangeException("CisloNaradi", "Cislo naradi musi byt kladne");
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
            ApplyChange(new CislovaneNaradiPrijatoNaVydejnuEvent
            {
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NaradiId = cmd.NaradiId,
                KodDodavatele = cmd.KodDodavatele,
                CisloNaradi = cmd.CisloNaradi,
                PrijemZeSkladu = cmd.PrijemZeSkladu,
                CenaNova = cmd.CenaNova,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto()
            });
            if (cmd.PrijemZeSkladu)
            {
                ApplyChange(new NastalaPotrebaUpravitStavNaSkladeEvent
                {
                    NaradiId = cmd.NaradiId,
                    TypZmeny = TypZmenyNaSklade.SnizitStav,
                    Hodnota = 1
                });
            }
        }
    }
}
