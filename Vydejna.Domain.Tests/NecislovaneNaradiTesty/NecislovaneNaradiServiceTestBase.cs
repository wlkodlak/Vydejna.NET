using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Domain.NecislovaneNaradi;
using Vydejna.Domain.ObecneNaradi;
using Vydejna.Domain.Tests.NaradiObecneTesty;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    public class NecislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<NecislovaneNaradiAggregate, NecislovaneNaradiService>
    {
        protected Guid _naradiId;
        protected List<object> _given;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _naradiId = new Guid("5844abcd-0000-0000-0000-111122223333");
            _given = new List<object>();
        }

        protected override NecislovaneNaradiService CreateService()
        {
            return new NecislovaneNaradiService(_repository, _time);
        }

        protected override void Execute<T>(T cmd)
        {
            _repository.AddEvents(_naradiId.ToId(), _given.ToArray());
            base.Execute<T>(cmd);
            AllowOnlySave(_naradiId);
        }

        protected SkupinaNecislovanehoNaradiDto Kus(DateTime datum, decimal cena, char cerstvost, int pocet)
        {
            return new SkupinaNecislovanehoNaradiDto
            {
                Datum = datum,
                Cena = cena,
                Cerstvost = CerstvostChar(cerstvost),
                Pocet = pocet
            };
        }

        protected void OcekavaneKusy<T>(Func<T, IList<SkupinaNecislovanehoNaradiDto>> extraktor, params SkupinaNecislovanehoNaradiDto[] ocekavano)
        {
            var realne = extraktor(NewEventOfType<T>());
            Assert.IsNotNull(realne, "Pouzite kusy");
            var realneStringy = string.Join("\r\n", realne.Select(StringOcekavanychKusu).OrderBy(s => s));
            var ocekavaneStringy = string.Join("\r\n", ocekavano.Select(StringOcekavanychKusu).OrderBy(s => s));
            Assert.AreEqual(ocekavaneStringy, realneStringy);
        }

        private string CerstvostChar(char cerstvost)
        {
            switch (cerstvost)
            {
                case 'N': return "Nove";
                case 'O': return "Opravene";
                default: return "Pouzite";
            }
        }

        private char CerstvostChar(string cerstvost)
        {
            switch (cerstvost)
            {
                case "Nove": return 'N';
                case "Opravene": return 'O';
                default: return 'P';
            }
        }

        private string StringOcekavanychKusu(SkupinaNecislovanehoNaradiDto dto)
        {
            return string.Format("{0:yyyyMMdd}{1}{2}x{3:0.00}",
                dto.Datum, CerstvostChar(dto.Cerstvost), dto.Pocet, dto.Cena);
        }

        protected DateTime Datum(int den)
        {
            return GetUtcTime().Date.AddDays(-90 + den);
        }

        protected NecislovaneNaradiPrijatoNaVydejnuEvent Prijate(int pocet, decimal cena = 10m, int datum = 0)
        {
            var evnt = new NecislovaneNaradiPrijatoNaVydejnuEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                KodDodavatele = "D88",
                CelkovaCenaNova = pocet * cena,
                PrijemZeSkladu = false,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena, 'N', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiVydanoDoVyrobyEvent Vydane(int pocet, decimal? cena = null, int datum = 0, string pracoviste = "84772140")
        {
            var prev = Prijate(pocet, 10m, datum);
            var evnt = new NecislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                KodPracoviste = pracoviste,
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaPracovisti(pracoviste).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 10m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPrijatoZVyrobyEvent Vracene(int pocet, decimal? cena = null, int datum = 0)
        {
            var prev = Vydane(pocet, 5m, datum, "84772140");
            var evnt = new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                StavNaradi = StavNaradi.Neopravitelne,
                KodPracoviste = "84772140",
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPrijatoZVyrobyEvent Poskozene(int pocet, decimal? cena = 0m, int datum = 0)
        {
            var prev = Vydane(pocet, 5m, datum, "84772140");
            var evnt = new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                StavNaradi = StavNaradi.Neopravitelne,
                KodPracoviste = "84772140",
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPrijatoZVyrobyEvent Znicene(int pocet, decimal? cena = 0m, int datum = 0)
        {
            var prev = Vydane(pocet, 5m, datum, "84772140");
            var evnt = new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                StavNaradi = StavNaradi.Neopravitelne,
                KodPracoviste = "84772140",
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPredanoKeSesrotovaniEvent Sesrotovane(int pocet, int datum = 0)
        {
            var prev = Znicene(pocet, 0m, datum);
            var evnt = new NecislovaneNaradiPredanoKeSesrotovaniEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = 0,
                NaradiId = _naradiId,
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = 0,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), 0, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPredanoKOpraveEvent Opravovane(int pocet, decimal? cena = 0m, int datum = 0, string dodavatel = "D48", string objednavka = "111/2014")
        {
            var prev = Poskozene(pocet, 0m, datum);
            var evnt = new NecislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                TypOpravy = TypOpravy.Oprava,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TerminDodani = Datum(datum).AddDays(30),
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaOprave(TypOpravy.Oprava, dodavatel, objednavka).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPrijatoZOpravyEvent Opravene(int pocet, decimal? cena = 0m, int datum = 0)
        {
            var prev = Opravovane(pocet, 0m, datum);
            var evnt = new NecislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                TypOpravy = TypOpravy.Oprava,
                KodDodavatele = prev.KodDodavatele,
                Objednavka = prev.Objednavka,
                DodaciList = "111d/2014",
                Opraveno = StavNaradiPoOprave.Opraveno,
                StavNaradi = StavNaradi.VPoradku,
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }

        protected NecislovaneNaradiPrijatoZOpravyEvent Neopravitelne(int pocet, decimal? cena = 0m, int datum = 0)
        {
            var prev = Opravovane(pocet, 0m, datum);
            var evnt = new NecislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                TypOpravy = TypOpravy.Oprava,
                KodDodavatele = prev.KodDodavatele,
                Objednavka = prev.Objednavka,
                DodaciList = "111d/2014",
                Opraveno = StavNaradiPoOprave.Neopravitelne,
                StavNaradi = StavNaradi.Neopravitelne,
                CelkovaCenaPredchozi = prev.CelkovaCenaNova,
                CelkovaCenaNova = cena.HasValue ? pocet * cena.Value : prev.CelkovaCenaNova,
                PredchoziUmisteni = prev.NoveUmisteni,
                PouziteKusy = prev.NoveKusy,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena ?? 5m, 'P', pocet) }
            };
            _given.Add(evnt);
            return evnt;
        }
    }
}
