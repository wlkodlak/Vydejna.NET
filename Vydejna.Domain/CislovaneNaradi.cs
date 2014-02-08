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
            _cena = evnt.CenaNova;
        }

        public void Execute(CislovaneNaradiPrijmoutZVyrobyCommand cmd, ITime time)
        {
            if (_cisloNaradi == 0)
                throw new InvalidOperationException("Naradi s timto cislem jeste neexistuje");
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                throw new ArgumentOutOfRangeException("CenaNova", "Chybi zdrojove pracoviste");
            if (cmd.StavNaradi == StavNaradi.Neurcen)
                throw new ArgumentOutOfRangeException("StavNaradi", "Stav naradi neni urcen");
            else if (cmd.StavNaradi == StavNaradi.VPoradku)
            {
                if (!string.IsNullOrEmpty(cmd.KodVady))
                    throw new ArgumentOutOfRangeException("KodVady", "Naradi v poradku nesmi mit uvedenou vadu");
            }
            {
                if (string.IsNullOrEmpty(cmd.KodVady))
                    throw new ArgumentOutOfRangeException("KodVady", "Poskozene musi mit uvedenou vadu");
            }
            if (_umisteni != UmisteniNaradi.NaPracovisti(cmd.KodPracoviste))
                throw new InvalidOperationException("Naradi neni na pracovisti");
            ApplyChange(new CislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NaradiId = cmd.NaradiId,
                CisloNaradi = cmd.CisloNaradi,
                CenaNova = cmd.CenaNova,
                KodPracoviste = cmd.KodPracoviste,
                PredchoziUmisteni = _umisteni.Dto(),
                CenaPredchozi = _cena,
                StavNaradi = cmd.StavNaradi,
                KodVady = cmd.KodVady,
                NoveUmisteni = UmisteniNaradi.NaVydejne(cmd.StavNaradi).Dto()
            });
        }

        private void ApplyChange(CislovaneNaradiPrijatoZVyrobyEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
            _cena = evnt.CenaNova;
        }

        public void Execute(CislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            var datum = time.GetUtcTime();
            if (_cisloNaradi == 0)
                throw new InvalidOperationException("Naradi s timto cislem jeste neexistuje");
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                throw new ArgumentOutOfRangeException("Objednavka", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                throw new ArgumentOutOfRangeException("KodDodavatele", "Nutne zadat dodavatele");
            if (cmd.TerminDodani < datum.Date)
                throw new ArgumentOutOfRangeException("TerminDodani", "Termin dodani nesmi byt v minulosti");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                throw new ArgumentOutOfRangeException("TypOpravy", "Nutne urcit typ opravy");
            if (_umisteni != UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit))
                throw new InvalidOperationException("Naradi neni urceno k oprave");
            ApplyChange(new CislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                Datum = datum,
                NaradiId = cmd.NaradiId,
                CisloNaradi = cmd.CisloNaradi,
                CenaNova = cmd.CenaNova,
                KodDodavatele = cmd.KodDodavatele,
                Objednavka = cmd.Objednavka,
                TerminDodani = cmd.TerminDodani,
                TypOpravy = cmd.TypOpravy,
                PredchoziUmisteni = _umisteni.Dto(),
                CenaPredchozi = _cena,
                NoveUmisteni = UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka).Dto()
            });
        }

        private void ApplyChange(CislovaneNaradiPredanoKOpraveEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
            _cena = evnt.CenaNova;
        }

        public void Execute(CislovaneNaradiPrijmoutZOpravyCommand cmd, ITime time)
        {
            if (_cisloNaradi == 0)
                throw new InvalidOperationException("Naradi s timto cislem jeste neexistuje");
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                throw new ArgumentOutOfRangeException("Objednavka", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.DodaciList))
                throw new ArgumentOutOfRangeException("DodaciList", "Nutne zadat cislo dodaciho listu");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                throw new ArgumentOutOfRangeException("KodDodavatele", "Nutne zadat dodavatele");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                throw new ArgumentOutOfRangeException("TypOpravy", "Nutne urcit typ opravy");
            if (cmd.Opraveno == StavNaradiPoOprave.Neurcen)
                throw new ArgumentOutOfRangeException("Opraveno", "Nutne urcit vysledek opravy");
            if (_umisteni != UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka))
                throw new InvalidOperationException("Naradi neni na oprave nebo nepatri teto objednavce");
            var novyStav = (cmd.Opraveno == StavNaradiPoOprave.Neopravitelne) ? StavNaradi.Neopravitelne : StavNaradi.VPoradku;
            ApplyChange(new CislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NaradiId = cmd.NaradiId,
                CisloNaradi = cmd.CisloNaradi,
                CenaNova = cmd.CenaNova,
                KodDodavatele = cmd.KodDodavatele,
                Objednavka = cmd.Objednavka,
                TypOpravy = cmd.TypOpravy,
                PredchoziUmisteni = _umisteni.Dto(),
                CenaPredchozi = _cena,
                Opraveno = cmd.Opraveno,
                DodaciList = cmd.DodaciList,
                StavNaradi = novyStav,
                NoveUmisteni = UmisteniNaradi.NaVydejne(novyStav).Dto()
            });
        }

        private void ApplyChange(CislovaneNaradiPrijatoZOpravyEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
            _cena = evnt.CenaNova;
        }

        public void Execute(CislovaneNaradiPredatKeSesrotovaniCommand cmd, ITime time)
        {
            if (_cisloNaradi == 0)
                throw new InvalidOperationException("Naradi s timto cislem jeste neexistuje");
            if (_umisteni != UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne))
                throw new InvalidOperationException("Naradi neni na vydejne jako v poradku");
            ApplyChange(new CislovaneNaradiPredanoKeSesrotovaniEvent
            {
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NaradiId = cmd.NaradiId,
                CisloNaradi = cmd.CisloNaradi,
                PredchoziUmisteni = _umisteni.Dto(),
                CenaPredchozi = _cena,
                NoveUmisteni = UmisteniNaradi.VeSrotu().Dto()
            });
        }

        private void ApplyChange(CislovaneNaradiPredanoKeSesrotovaniEvent evnt)
        {
            RecordChange(evnt);
            _umisteni = UmisteniNaradi.Dto(evnt.NoveUmisteni);
            _cena = 0;
        }
    }
}
