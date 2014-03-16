using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class PrijemZOpravyTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CislovaneNaradiMusiExistovat()
        {
            var naradi = Id("8394bb2a");
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            });
            ChybaStavu("NOTFOUND", "CisloNaradi");
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = -4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            });
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void NutneZadatObjednavku()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "",
                DodaciList = "482d/2014"
            });
            ChybaValidace("REQUIRED", "Objednavka");
        }

        [TestMethod]
        public void NutneZadatDodaciList()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = ""
            });
            ChybaValidace("REQUIRED", "DodaciList");
        }

        [TestMethod]
        public void NutneZadatDodavatele()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            });
            ChybaValidace("REQUIRED", "KodDodavatele");
        }

        [TestMethod]
        public void NutneZadatTypOpravy()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            });
            ChybaValidace("REQUIRED", "TypOpravy");
        }

        [TestMethod]
        public void NutneZadatVysledekOpravy()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            Execute(new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            });
            ChybaValidace("REQUIRED", "Opraveno");
        }

        [TestMethod]
        public void NaradiNaStejneObjednavceLzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            };
            Execute(cmd);
            Event<CislovaneNaradiPrijatoZOpravyEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiNaJineObjednavceJeNepouzitelne()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "999/2014",
                DodaciList = "999d/2014"
            };
            Execute(cmd);
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void NaradiNaVydejneJeNepouzitelne()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "123/2014",
                DodaciList = "123d/2014"
            };
            Execute(cmd);
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void VUdalostiOdpovidajiHodnotyZPrikazu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.TypOpravy, udalost.TypOpravy, "TypOpravy");
            Assert.AreEqual(cmd.Opraveno, udalost.Opraveno, "Opraveno");
            Assert.AreEqual(cmd.KodDodavatele, udalost.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.Objednavka, udalost.Objednavka, "Objednavka");
            Assert.AreEqual(cmd.DodaciList, udalost.DodaciList, "DodaciList");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void JeMozneZiskatZOpravyNeopravitelneNaradi()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Oprava, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Oprava,
                Opraveno = StavNaradiPoOprave.Neopravitelne,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(cmd.TypOpravy, udalost.TypOpravy, "TypOpravy");
            Assert.AreEqual(cmd.Opraveno, udalost.Opraveno, "Opraveno");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void VUdalostiSeGenerujiAutomatickeHodnoty()
        {
            var naradi = Id("8394bb2a");
            Given(naradi,
                EvtPrijato(naradi, 4),
                EvtVydano(naradi, 4),
                EvtVraceno(naradi, 4, stav: StavNaradi.NutnoOpravit, vada: "2"),
                EvtOprava(naradi, 4, typ: TypOpravy.Reklamace, cenaPo: 120m, dodavatel: "D88", objednavka: "482/2014"));
            var cmd = new CislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4,
                CenaNova = 4.33m,
                TypOpravy = TypOpravy.Reklamace,
                Opraveno = StavNaradiPoOprave.Opraveno,
                KodDodavatele = "D88",
                Objednavka = "482/2014",
                DodaciList = "482d/2014"
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(120m, udalost.CenaPredchozi, "CenaPredchozi");
            Assert.AreEqual(UmisteniNaradi.NaOprave(TypOpravy.Reklamace, cmd.KodDodavatele, cmd.Objednavka).Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(5, udalost.Verze, "Verze");
        }
    }
}
