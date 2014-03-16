using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

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
            ChybaStavu("NOTFOUND", "CisloNaradi");
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
            ChybaValidace("RANGE", "CenaNova");
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
            ChybaValidace("REQUIRED", "KodPracoviste");
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
            ChybaStavu("RANGE", "Umisteni");
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
            ChybaStavu("RANGE", "Umisteni");
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
            Assert.AreEqual(2, udalost.Verze, "Verze");
        }
    }
}
