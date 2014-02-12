using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NecislovaneNaradi : EventSourcedGuidAggregate
    {
        private Dictionary<UmisteniNaradi, MnozinaNecislovanehoNaradi> _obsahUmistneni;

        public NecislovaneNaradi()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
            _obsahUmistneni = new Dictionary<UmisteniNaradi, MnozinaNecislovanehoNaradi>();
        }

        protected override object CreateSnapshot()
        {
            return new NecislovaneNaradiSnapshot_v1 { 
                Version = CurrentVersion
            };
        }

        protected override int LoadFromSnapshot(object snapshotObject)
        {
            var snapshot = snapshotObject as NecislovaneNaradiSnapshot_v1;
            if (snapshot == null)
                return 0;
            if (snapshot.Rozlozeni != null)
            {
                foreach (var obsah in snapshot.Rozlozeni)
                {
                    var mnozina = new MnozinaNecislovanehoNaradi();
                    foreach (var skupinaDto in obsah.Skupiny)
                        mnozina.Pridat(skupinaDto.ToValue());
                    mnozina.PocetCelkem = obsah.PocetCelkem;
                    _obsahUmistneni[obsah.Umisteni.ToValue()] = mnozina;
                }
            }
            return snapshot.Version;
        }

        private MnozinaNecislovanehoNaradi NajitUmisteni(UmisteniNaradi umisteni, bool vytvorit)
        {
            MnozinaNecislovanehoNaradi mnozina;
            if (_obsahUmistneni.TryGetValue(umisteni, out mnozina))
                return mnozina;
            else if (!vytvorit)
                return MnozinaNecislovanehoNaradi.NullObject;
            else
            {
                mnozina = new MnozinaNecislovanehoNaradi();
                _obsahUmistneni[umisteni] = mnozina;
                return mnozina;
            }
        }

        public void Execute(NecislovaneNaradiPrijmoutNaVydejnuCommand cmd, ITime time)
        {
            var evnt = new NecislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                KodDodavatele = cmd.KodDodavatele,
                Pocet = cmd.Pocet,
                PrijemZeSkladu = cmd.PrijemZeSkladu,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                CelkovaCenaNova = cmd.Pocet * cmd.CenaNova,
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>()
            };
            evnt.NoveKusy.Add(new SkupinaNecislovanehoNaradi(evnt.Datum, evnt.CenaNova, CerstvostNecislovanehoNaradi.Nove, evnt.Pocet).Dto());
            ApplyChange(evnt);

            if (cmd.PrijemZeSkladu)
            {
                ApplyChange(new NastalaPotrebaUpravitStavNaSkladeEvent
                {
                    NaradiId = cmd.NaradiId,
                    TypZmeny = TypZmenyNaSklade.SnizitStav,
                    Hodnota = cmd.Pocet
                });
            }
        }

        private void ApplyChange(NecislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            RecordChange(evnt);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.NoveKusy)
                novaMnozina.Pridat(skupinaDto.ToValue());

        }

        private void ApplyChange(NastalaPotrebaUpravitStavNaSkladeEvent evnt)
        {
            RecordChange(evnt);
        }

        public void Execute(NecislovaneNaradiVydatDoVyrobyCommand cmd, ITime time)
        {
            var predchoziUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku);
            var noveUmisteni = UmisteniNaradi.NaPracovisti(cmd.KodPracoviste);
            var evnt = new NecislovaneNaradiVydanoDoVyrobyEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                KodPracoviste = cmd.KodPracoviste,
                Pocet = cmd.Pocet,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                PredchoziUmisteni = predchoziUmisteni.Dto(),
                NoveUmisteni = noveUmisteni.Dto()
            };
            var mnozina = NajitUmisteni(predchoziUmisteni, false);
            if (cmd.Pocet > mnozina.PocetCelkem)
                throw new DomainErrorException("Pocet", "RANGE", "Na vydejne neni dostatek naradi");
            var pouziteKusy = mnozina.Pouzit(cmd.Pocet);
            evnt.PouziteKusy = pouziteKusy.Select(k => k.Dto()).ToList();
            evnt.NoveKusy = pouziteKusy.Select(k => NovaSkupina(k, cmd.CenaNova, 'P').Dto()).ToList();
            evnt.CelkovaCenaPredchozi = pouziteKusy.Sum(k => k.Cena * k.Pocet);
            evnt.CelkovaCenaNova = cmd.CenaNova.HasValue ? cmd.Pocet * cmd.CenaNova.Value : evnt.CelkovaCenaPredchozi;
            ApplyChange(evnt);
        }

        private void ApplyChange(NecislovaneNaradiVydanoDoVyrobyEvent evnt)
        {
            RecordChange(evnt);
            var predchoziMnozina = NajitUmisteni(evnt.PredchoziUmisteni.ToValue(), true);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.PouziteKusy)
                predchoziMnozina.Odebrat(skupinaDto.ToValue());
            foreach (var skupinaDto in evnt.NoveKusy)
                novaMnozina.Pridat(skupinaDto.ToValue());
        }

        public void Execute(NecislovaneNaradiPrijmoutZVyrobyCommand cmd, ITime time)
        {
            var predchoziUmisteni = UmisteniNaradi.NaPracovisti(cmd.KodPracoviste);
            var noveUmisteni = UmisteniNaradi.NaVydejne(cmd.StavNaradi);
            var evnt = new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                KodPracoviste = cmd.KodPracoviste,
                StavNaradi = cmd.StavNaradi,
                Pocet = cmd.Pocet,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                PredchoziUmisteni = predchoziUmisteni.Dto(),
                NoveUmisteni = noveUmisteni.Dto()
            };
            var mnozina = NajitUmisteni(predchoziUmisteni, false);
            if (cmd.Pocet > mnozina.PocetCelkem)
                throw new DomainErrorException("Pocet", "RANGE", "Na vydejne neni dostatek naradi");
            var pouziteKusy = mnozina.Pouzit(cmd.Pocet);
            evnt.PouziteKusy = pouziteKusy.Select(k => k.Dto()).ToList();
            evnt.NoveKusy = pouziteKusy.Select(k => NovaSkupina(k, cmd.CenaNova, null).Dto()).ToList();
            evnt.CelkovaCenaPredchozi = pouziteKusy.Sum(k => k.Cena * k.Pocet);
            evnt.CelkovaCenaNova = cmd.CenaNova.HasValue ? cmd.Pocet * cmd.CenaNova.Value : evnt.CelkovaCenaPredchozi;
            ApplyChange(evnt);
        }

        private void ApplyChange(NecislovaneNaradiPrijatoZVyrobyEvent evnt)
        {
            RecordChange(evnt);
            var predchoziMnozina = NajitUmisteni(evnt.PredchoziUmisteni.ToValue(), true);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.PouziteKusy)
                predchoziMnozina.Odebrat(skupinaDto.ToValue());
            foreach (var skupinaDto in evnt.NoveKusy)
                novaMnozina.Pridat(skupinaDto.ToValue());
        }

        public void Execute(NecislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            var predchoziUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit);
            var noveUmisteni = UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka);
            var evnt = new NecislovaneNaradiPredanoKOpraveEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                TypOpravy = cmd.TypOpravy,
                KodDodavatele = cmd.KodDodavatele,
                Objednavka = cmd.Objednavka,
                TerminDodani = cmd.TerminDodani,
                Pocet = cmd.Pocet,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                PredchoziUmisteni = predchoziUmisteni.Dto(),
                NoveUmisteni = noveUmisteni.Dto()
            };
            var mnozina = NajitUmisteni(predchoziUmisteni, false);
            if (cmd.Pocet > mnozina.PocetCelkem)
                throw new DomainErrorException("Pocet", "RANGE", "Na vydejne neni dostatek naradi");
            var pouziteKusy = mnozina.Pouzit(cmd.Pocet);
            evnt.PouziteKusy = pouziteKusy.Select(k => k.Dto()).ToList();
            evnt.NoveKusy = pouziteKusy.Select(k => NovaSkupina(k, cmd.CenaNova, null).Dto()).ToList();
            evnt.CelkovaCenaPredchozi = pouziteKusy.Sum(k => k.Cena * k.Pocet);
            evnt.CelkovaCenaNova = cmd.CenaNova.HasValue ? cmd.Pocet * cmd.CenaNova.Value : evnt.CelkovaCenaPredchozi;
            ApplyChange(evnt);
        }

        private void ApplyChange(NecislovaneNaradiPredanoKOpraveEvent evnt)
        {
            RecordChange(evnt);
            var predchoziMnozina = NajitUmisteni(evnt.PredchoziUmisteni.ToValue(), true);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.PouziteKusy)
                predchoziMnozina.Odebrat(skupinaDto.ToValue());
            foreach (var skupinaDto in evnt.NoveKusy)
                novaMnozina.Pridat(skupinaDto.ToValue());
        }

        public void Execute(NecislovaneNaradiPrijmoutZOpravyCommand cmd, ITime time)
        {
            var stavNaradi = cmd.Opraveno == StavNaradiPoOprave.Neopravitelne ? StavNaradi.Neopravitelne : StavNaradi.VPoradku;
            var predchoziUmisteni = UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka);
            var noveUmisteni = UmisteniNaradi.NaVydejne(stavNaradi);
            var evnt = new NecislovaneNaradiPrijatoZOpravyEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                TypOpravy = cmd.TypOpravy,
                KodDodavatele = cmd.KodDodavatele,
                Objednavka = cmd.Objednavka,
                DodaciList = cmd.DodaciList,
                Opraveno = cmd.Opraveno,
                StavNaradi = stavNaradi,
                Pocet = cmd.Pocet,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                PredchoziUmisteni = predchoziUmisteni.Dto(),
                NoveUmisteni = noveUmisteni.Dto()
            };
            var mnozina = NajitUmisteni(predchoziUmisteni, false);
            if (cmd.Pocet > mnozina.PocetCelkem)
                throw new DomainErrorException("Pocet", "RANGE", "Na vydejne neni dostatek naradi");
            var pouziteKusy = mnozina.Pouzit(cmd.Pocet);
            evnt.PouziteKusy = pouziteKusy.Select(k => k.Dto()).ToList();
            evnt.NoveKusy = pouziteKusy.Select(k => NovaSkupina(k, cmd.CenaNova, null).Dto()).ToList();
            evnt.CelkovaCenaPredchozi = pouziteKusy.Sum(k => k.Cena * k.Pocet);
            evnt.CelkovaCenaNova = cmd.CenaNova.HasValue ? cmd.Pocet * cmd.CenaNova.Value : evnt.CelkovaCenaPredchozi;
            ApplyChange(evnt);
        }

        private void ApplyChange(NecislovaneNaradiPrijatoZOpravyEvent evnt)
        {
            RecordChange(evnt);
            var predchoziMnozina = NajitUmisteni(evnt.PredchoziUmisteni.ToValue(), true);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.PouziteKusy)
                predchoziMnozina.Odebrat(skupinaDto.ToValue());
            foreach (var skupinaDto in evnt.NoveKusy)
                novaMnozina.Pridat(skupinaDto.ToValue());
        }

        public void Execute(NecislovaneNaradiPredatKeSesrotovaniCommand cmd, ITime time)
        {
            var predchoziUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne);
            var noveUmisteni = UmisteniNaradi.VeSrotu();
            var evnt = new NecislovaneNaradiPredanoKeSesrotovaniEvent
            {
                NaradiId = cmd.NaradiId,
                Pocet = cmd.Pocet,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                PredchoziUmisteni = predchoziUmisteni.Dto(),
                NoveUmisteni = noveUmisteni.Dto()
            };
            var mnozina = NajitUmisteni(predchoziUmisteni, false);
            if (cmd.Pocet > mnozina.PocetCelkem)
                throw new DomainErrorException("Pocet", "RANGE", "Na vydejne neni dostatek naradi");
            var pouziteKusy = mnozina.Pouzit(cmd.Pocet);
            evnt.PouziteKusy = pouziteKusy.Select(k => k.Dto()).ToList();
            evnt.CelkovaCenaPredchozi = pouziteKusy.Sum(k => k.Cena * k.Pocet);
            ApplyChange(evnt);
        }

        private void ApplyChange(NecislovaneNaradiPredanoKeSesrotovaniEvent evnt)
        {
            RecordChange(evnt);
            var predchoziMnozina = NajitUmisteni(evnt.PredchoziUmisteni.ToValue(), true);
            var novaMnozina = NajitUmisteni(evnt.NoveUmisteni.ToValue(), true);
            foreach (var skupinaDto in evnt.PouziteKusy)
                predchoziMnozina.Odebrat(skupinaDto.ToValue());
        }

        private SkupinaNecislovanehoNaradi NovaSkupina(SkupinaNecislovanehoNaradi puvodni, decimal? novaCena, char? novaCerstvost)
        {
            if (!novaCena.HasValue && !novaCerstvost.HasValue)
                return puvodni;
            var cena = novaCena ?? puvodni.Cena;
            var cerstvost = novaCerstvost.HasValue ? CerstvostChar(novaCerstvost.Value) : puvodni.Cerstvost;
            return new SkupinaNecislovanehoNaradi(puvodni.DatumCerstvosti, cena, cerstvost, puvodni.Pocet);
        }

        private CerstvostNecislovanehoNaradi CerstvostChar(char cerstvost)
        {
            switch (cerstvost)
            {
                case 'N': return CerstvostNecislovanehoNaradi.Nove;
                case 'O': return CerstvostNecislovanehoNaradi.Opravene;
                default: return CerstvostNecislovanehoNaradi.Pouzite;
            }
        }
    }
}
