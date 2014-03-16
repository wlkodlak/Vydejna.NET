using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class PrijemZVyrobyTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CislovaneNaradiMusiExistovat()
        {
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.VPoradku,
                KodVady = null
            });
            ChybaStavu("NOTFOUND", "CisloNaradi");
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = -4.33m,
                StavNaradi = StavNaradi.VPoradku,
                KodVady = null
            });
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void KodPracovisteNesmiChybet()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.VPoradku,
                KodVady = null
            });
            ChybaValidace("REQUIRED", "KodPracoviste");
        }

        [TestMethod]
        public void UNaradiVPoradkuNesmiBytVada()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.VPoradku,
                KodVady = "9"
            });
            ChybaValidace("RANGE", "KodVady");
        }

        [TestMethod]
        public void UPozkozenehoNaradiNesmiChybetVada()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.NutnoOpravit,
                KodVady = ""
            });
            ChybaValidace("REQUIRED", "KodVady");
        }

        [TestMethod]
        public void MusiBytUvedenStavNaradiPriVraceni()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "84930120",
                CenaNova = 4.33m,
                KodVady = "9"
            });
            ChybaValidace("REQUIRED", "StavNaradi");
        }

        [TestMethod]
        public void NaradiNaSpravnemPracovistiLzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4, pracoviste: "09842333"));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.NutnoOpravit,
                KodVady = "5"
            });
            Event<CislovaneNaradiPrijatoZVyrobyEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiNaJinemPracovistiNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4, "99999999"));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.NutnoOpravit,
                KodVady = "5"
            });
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void NevydaneNaradiNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m,
                StavNaradi = StavNaradi.NutnoOpravit,
                KodVady = "5"
            });
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void VUdalostiOdpovidajiHodnotyZPrikazu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4, pracoviste: "09842333"));
            var cmd = new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m,
                KodVady = "4",
                StavNaradi = StavNaradi.NutnoOpravit
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.KodPracoviste, udalost.KodPracoviste, "KodPracoviste");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.KodVady, udalost.KodVady, "KodVady");
            Assert.AreEqual(cmd.StavNaradi, udalost.StavNaradi, "StavNaradi");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(cmd.StavNaradi).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void VUdalostiSeGenerujiAutomatickeHodnoty()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4, pracoviste: "09842333", cenaPo: 120m));
            var cmd = new CislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                KodPracoviste = "09842333",
                CenaNova = 4.33m,
                KodVady = "4",
                StavNaradi = StavNaradi.NutnoOpravit
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(120m, udalost.CenaPredchozi, "CenaPredchozi");
            Assert.AreEqual(UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
        }
    }
}
