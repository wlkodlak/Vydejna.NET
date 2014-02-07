using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class VydejDoVyrobyTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CislovaneNaradiMusiExistovat()
        {
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = 4.33m
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = -4.33m
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void KodPracovisteNesmiChybet()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "",
                CenaNova = 4.33m
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void NoveNaradiLzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            });
            Event<CislovaneNaradiVydanoDoVyrobyEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiPrijateVPoradkuLzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVraceno(naradi, 4));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            });
            Event<CislovaneNaradiVydanoDoVyrobyEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiPrijatePoskozeneNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void NaradiVeSrotuNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtSrotovano(naradi, 4));
            Execute(new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void VUdalostiOdpovidajiHodnotyZPrikazu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            var cmd = new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.KodPracoviste, udalost.KodPracoviste, "KodPracoviste");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void VUdalostiSeGenerujiAutomatickeHodnoty()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4, cena: 300m));
            var cmd = new CislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(300m, udalost.CenaPredchozi, "CenaPredchozi");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
        }

        /*
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         */

        private CislovaneNaradiPrijatoNaVydejnuEvent EvtPrijato(Guid naradi, int cisloNaradi,
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

        private CislovaneNaradiPrijatoZVyrobyEvent EvtVraceno(Guid naradi, int cisloNaradi,
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

        private CislovaneNaradiPredanoKeSesrotovaniEvent EvtSrotovano(Guid naradi, int cisloNaradi,
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
    }
}
