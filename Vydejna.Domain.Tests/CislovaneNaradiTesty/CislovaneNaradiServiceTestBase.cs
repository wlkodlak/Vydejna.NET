using System;
using Vydejna.Contracts;
using Vydejna.Domain.CislovaneNaradi;
using Vydejna.Domain.ObecneNaradi;
using Vydejna.Domain.Tests.NaradiObecneTesty;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    public class CislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<CislovaneNaradiAggregate, CislovaneNaradiService>
    {
        protected override CislovaneNaradiService CreateService()
        {
            return new CislovaneNaradiService(_repository, _time);
        }

        protected CislovaneNaradiPrijatoNaVydejnuEvent EvtPrijato(Guid naradi, int cisloNaradi,
           string kodDodavatele = "D43", decimal cena = 283m)
        {
            return new CislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                KodDodavatele = kodDodavatele,
                CenaNova = cena,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                PrijemZeSkladu = false
            };
        }

        protected CislovaneNaradiVydanoDoVyrobyEvent EvtVydano(Guid naradi, int cisloNaradi,
            string pracoviste = "88339430", decimal cenaPred = 100m, decimal cenaPo = 100m)
        {
            return new CislovaneNaradiVydanoDoVyrobyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                KodPracoviste = pracoviste,
                NoveUmisteni = UmisteniNaradi.NaPracovisti(pracoviste).Dto()
            };
        }

        protected CislovaneNaradiPrijatoZVyrobyEvent EvtVraceno(Guid naradi, int cisloNaradi,
            string pracoviste = "88339430", StavNaradi stav = StavNaradi.VPoradku,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            string vada = null)
        {
            return new CislovaneNaradiPrijatoZVyrobyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodPracoviste = pracoviste,
                KodVady = vada,
                StavNaradi = stav,
                PredchoziUmisteni = UmisteniNaradi.NaPracovisti(pracoviste).Dto(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(stav).Dto()
            };
        }

        protected CislovaneNaradiPredanoKeSesrotovaniEvent EvtSrotovano(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, StavNaradi stav = StavNaradi.Neopravitelne)
        {
            return new CislovaneNaradiPredanoKeSesrotovaniEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(stav).Dto(),
                NoveUmisteni = UmisteniNaradi.VeSrotu().Dto()
            };
        }

        protected CislovaneNaradiPredanoKOpraveEvent EvtOprava(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            StavNaradi stav = StavNaradi.NutnoOpravit, TypOpravy typ = TypOpravy.Oprava,
            string dodavatel = "D43", string objednavka = "483/2013", DateTime termin = default(DateTime))
        {
            return new CislovaneNaradiPredanoKOpraveEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TerminDodani = termin == default(DateTime) ? GetUtcTime().Date.AddDays(15) : termin,
                TypOpravy = typ,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(stav).Dto(),
                NoveUmisteni = UmisteniNaradi.NaOprave(typ, dodavatel, objednavka).Dto()
            };
        }

        protected CislovaneNaradiPrijatoZOpravyEvent EvtOpraveno(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            TypOpravy typ = TypOpravy.Oprava, StavNaradiPoOprave opraveno = StavNaradiPoOprave.Opraveno,
            string dodavatel = "D43", string objednavka = "483/2013", string dodaciList = "483d/2013")
        {
            var stav = (opraveno == StavNaradiPoOprave.OpravaNepotrebna || opraveno == StavNaradiPoOprave.Opraveno) ? StavNaradi.VPoradku : StavNaradi.Neopravitelne;
            return new CislovaneNaradiPrijatoZOpravyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TypOpravy = typ,
                DodaciList = dodaciList,
                Opraveno = opraveno,
                StavNaradi = stav,
                PredchoziUmisteni = UmisteniNaradi.NaOprave(typ, dodavatel, objednavka).Dto(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(stav).Dto()
            };
        }
    }
}
