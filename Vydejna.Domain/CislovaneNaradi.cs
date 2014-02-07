using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;
using System.Reflection;

namespace Vydejna.Domain
{
    public class CislovaneNaradi : EventSourcedAggregate
    {
        private Guid _naradiId;
        private int _cisloNaradi;
        private UmisteniNaradi _umisteni;
        private decimal _cena;

        public CislovaneNaradi()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
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

        private void ApplyChange(CislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            RecordChange(evnt);
            _naradiId = evnt.NaradiId;
            _cisloNaradi = evnt.CisloNaradi;
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
            _cena = evnt.CenaNova;
        }

        private void ApplyChange(NastalaPotrebaUpravitStavNaSkladeEvent evnt)
        {
            RecordChange(evnt);
        }

        public void Execute(CislovaneNaradiVydatDoVyrobyCommand cmd, ITime time)
        {
            if (_cisloNaradi == 0)
                throw new InvalidOperationException("Naradi s timto cislem jeste neexistuje");
            if (cmd.CisloNaradi <= 0)
                throw new ArgumentOutOfRangeException("CisloNaradi", "Cislo naradi musi byt kladne");
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                throw new ArgumentOutOfRangeException("CenaNova", "Chybi cilove pracoviste");
            if (_umisteni != UmisteniNaradi.NaVydejne(StavNaradi.VPoradku))
                throw new InvalidOperationException("Naradi neni na vydejne jako v poradku");
            ApplyChange(new CislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NaradiId = cmd.NaradiId,
                CisloNaradi = cmd.CisloNaradi,
                CenaNova = cmd.CenaNova,
                KodPracoviste = cmd.KodPracoviste,
                PredchoziUmisteni = _umisteni.Dto(),
                CenaPredchozi = _cena,
                NoveUmisteni = UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto()
            });
        }

        private void ApplyChange(CislovaneNaradiVydanoDoVyrobyEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }

        private void ApplyChange(CislovaneNaradiPrijatoZVyrobyEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }

        private void ApplyChange(CislovaneNaradiPredanoKOpraveEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }

        private void ApplyChange(CislovaneNaradiPrijatoZOpravyEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }

        private void ApplyChange(CislovaneNaradiPredanoKeSesrotovaniEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
        }
    }
}
