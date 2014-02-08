using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class VydejNaOpravuTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CislovaneNaradiMusiExistovat()
        {
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = -4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void NutneZadatObjednavku()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void NutneZadatDodavatele()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void TerminNesmiBytVMinulosti()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(-1),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void NutneZadatTypOpravy()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15)
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void NaradiUrceneProOpravuLzePouzitProReklamaci()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Reklamace
            });
            Event<CislovaneNaradiPredanoKOpraveEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiVPoradkuNelzePoslatNaOpravu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.VPoradku, vada: null));
            Execute(new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Oprava
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void VUdalostiOdpovidajiHodnotyZPrikazu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9"));
            var cmd = new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Reklamace
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.KodDodavatele, udalost.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.Objednavka, udalost.Objednavka, "Objednavka");
            Assert.AreEqual(cmd.TerminDodani, udalost.TerminDodani, "TerminDodani");
            Assert.AreEqual(cmd.TypOpravy, udalost.TypOpravy, "TypOpravy");
            Assert.AreEqual(UmisteniNaradi.NaOprave(TypOpravy.Reklamace, cmd.KodDodavatele, cmd.Objednavka).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void VUdalostiSeGenerujiAutomatickeHodnoty()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "9", cenaPo: 240m));
            var cmd = new CislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4,
                CenaNova = 4.33m,
                KodDodavatele = "D94",
                Objednavka = "384/2014",
                TerminDodani = GetUtcTime().Date.AddDays(15),
                TypOpravy = TypOpravy.Reklamace
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(240m, udalost.CenaPredchozi, "CenaPredchozi");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit).Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
        }
    }
}
