using System;
using System.Collections.Generic;
using System.Linq;
using ServiceLib.Tests.TestUtils;
using ServiceLib;
using Vydejna.Contracts;
using Vydejna.Projections.NaradiNaObjednavceReadModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class NaradiNaObjednavceReadModelTest_NeexistujiciObjednavka : NaradiNaObjednavceReadModelTestBase
    {
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }

        [TestMethod]
        public void ObjednavkaNeexistuje()
        {
            Assert.AreEqual(false, _response.ObjednavkaExistuje);
        }

        [TestMethod]
        public void KopirovatUdajeZPozadavku()
        {
            Assert.IsNotNull(_response.Dodavatel);
            Assert.AreEqual("D005", _response.Dodavatel.Kod);
            Assert.AreEqual("3849/2014", _response.Objednavka);
        }

        [TestMethod]
        public void PrazdneUdajeObjednavky()
        {
            Assert.AreEqual(0, _response.PocetCelkem);
            Assert.AreEqual(0, _response.Seznam.Count);
            Assert.AreEqual<DateTime?>(null, _response.TerminDodani);
        }
    }

    [TestClass]
    public class NaradiNaObjednavceReadModelTest_JedineNaradiNaNoveObjednavce : NaradiNaObjednavceReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("0001", "847-8715", "prum. 18", "kotouc");
            SendDefinovanDodavatel("D005", "Opravar, s.r.o.");
            SendNaOpravu("0001", "D005", "3849/2014", 5, new DateTime(2014, 5, 18));
        }
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }
        [TestMethod]
        public void ObjednavkaExistuje()
        {
            Assert.AreEqual(true, _response.ObjednavkaExistuje);
        }
        [TestMethod]
        public void UdajeODodavateli()
        {
            Assert.IsNotNull(_response.Dodavatel);
            Assert.AreEqual("D005", _response.Dodavatel.Kod);
            Assert.AreEqual("Opravar, s.r.o.", _response.Dodavatel.Nazev);
        }
        [TestMethod]
        public void UdajeOObjednavce()
        {
            Assert.AreEqual("3849/2014", _response.Objednavka);
            Assert.AreEqual<DateTime?>(new DateTime(2014, 5, 18), _response.TerminDodani);
        }
        [TestMethod]
        public void PresneJedenDruhNaradi()
        {
            Assert.AreEqual(1, _response.Seznam.Count);
            Assert.AreEqual(5, _response.PocetCelkem);
        }
        [TestMethod]
        public void UdajeZDefiniceNaradi()
        {
            Assert.AreEqual(NaradiId("0001"), _response.Seznam[0].NaradiId);
            Assert.AreEqual("847-8715", _response.Seznam[0].Vykres);
            Assert.AreEqual("prum. 18", _response.Seznam[0].Rozmer);
            Assert.AreEqual("kotouc", _response.Seznam[0].Druh);
        }
        [TestMethod]
        public void PocetNaradi()
        {
            Assert.AreEqual(5, _response.Seznam[0].PocetCelkem);
            Assert.AreEqual(5, _response.Seznam[0].PocetNecislovanych);
            Assert.AreEqual(0, _response.Seznam[0].SeznamCislovanych.Count);
        }
    }

    [TestClass]
    public class NaradiNaObjednavceReadModelTest_ViceDruhuNaradi : NaradiNaObjednavceReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("0001", "847-8715", "prum. 18", "kotouc");
            SendDefinovanoNaradi("0002", "2847-9999a", "20x18", "");
            SendDefinovanoNaradi("0003", "390-1088", "o 10", "bruska");
            SendDefinovanDodavatel("D005", "Opravar, s.r.o.");
            SendNaOpravu("0001", "D005", "3849/2014", 5, new DateTime(2014, 5, 18));
            SendNaOpravu("0002", "D005", "3849/2014", 2, new DateTime(2014, 5, 18));
            SendNaOpravuCislovane("0003", "D005", "3849/2014", 4, new DateTime(2014, 5, 18));
        }
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }
        [TestMethod]
        public void NekolikDruhuNaradi()
        {
            var seznam = _response.Seznam.OrderBy(x => x.NaradiId).ToList();
            Assert.AreEqual(3, seznam.Count);
            Assert.AreEqual(NaradiId("0001"), seznam[0].NaradiId);
            Assert.AreEqual(NaradiId("0002"), seznam[1].NaradiId);
            Assert.AreEqual(NaradiId("0003"), seznam[2].NaradiId);
        }
        [TestMethod]
        public void CelkovyPocetKusu()
        {
            Assert.AreEqual(8, _response.PocetCelkem);
            var seznam = _response.Seznam.OrderBy(x => x.NaradiId).ToList();
            Assert.AreEqual(5, seznam[0].PocetCelkem);
            Assert.AreEqual(2, seznam[1].PocetCelkem);
            Assert.AreEqual(1, seznam[2].PocetCelkem);
            Assert.AreEqual(5, seznam[0].PocetNecislovanych);
            Assert.AreEqual(2, seznam[1].PocetNecislovanych);
            Assert.AreEqual(0, seznam[2].PocetNecislovanych);
            Assert.AreEqual(0, seznam[0].SeznamCislovanych.Count);
            Assert.AreEqual(0, seznam[1].SeznamCislovanych.Count);
            Assert.AreEqual(1, seznam[2].SeznamCislovanych.Count);
        }
    }

    [TestClass]
    public class NaradiNaObjednavceReadModelTest_UpravaJizOdeslanehoNaradi : NaradiNaObjednavceReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("0001", "847-8715", "prum. 18", "kotouc");
            SendDefinovanDodavatel("D005", "Opravar, s.r.o.");
            SendNaOpravu("0001", "D005", "3849/2014", 3, new DateTime(2014, 5, 18));
            SendNaOpravuCislovane("0001", "D005", "3849/2014", 3, new DateTime(2014, 5, 18));
            SendNaOpravu("0001", "D005", "3849/2014", 2, new DateTime(2014, 5, 18), 5);
        }
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }
        [TestMethod]
        public void NekolikDruhuNaradi()
        {
            Assert.AreEqual(1, _response.Seznam.Count);
            Assert.AreEqual(NaradiId("0001"), _response.Seznam[0].NaradiId);
        }
        [TestMethod]
        public void PoctyKusuSeUpravuji()
        {
            Assert.AreEqual(6, _response.PocetCelkem);
            Assert.AreEqual(6, _response.Seznam[0].PocetCelkem);
            Assert.AreEqual(5, _response.Seznam[0].PocetNecislovanych);
            Assert.AreEqual(1, _response.Seznam[0].SeznamCislovanych.Count);
        }
    }

    [TestClass]
    public class NaradiNaObjednavceReadModelTest_PrijemJizOdeslanehoNaradi : NaradiNaObjednavceReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("0002", "2847-9999a", "20x18", "");
            SendDefinovanoNaradi("0003", "390-1088", "o 10", "bruska");
            SendDefinovanDodavatel("D005", "Opravar, s.r.o.");
            SendNaOpravu("0001", "D005", "3849/2014", 5, new DateTime(2014, 5, 18));
            SendNaOpravuCislovane("0001", "D005", "3849/2014", 3, new DateTime(2014, 5, 18));
            SendNaOpravu("0002", "D005", "3849/2014", 2, new DateTime(2014, 5, 18));
            SendZOpravy("0001", "D005", "3849/2014", 2, 3);
            SendZOpravyCislovane("0001", "D005", "3849/2014", 3);
        }
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }
        [TestMethod]
        public void PoctyKusuSeSnizuji()
        {
            Assert.AreEqual(5, _response.PocetCelkem);
            var seznam = _response.Seznam.OrderBy(x => x.NaradiId).ToList();
            Assert.AreEqual(3, seznam[0].PocetCelkem);
            Assert.AreEqual(3, seznam[0].PocetNecislovanych);
            Assert.AreEqual(0, seznam[0].SeznamCislovanych.Count);
        }
    }

    [TestClass]
    public class NaradiNaObjednavceReadModelTest_PrijemVsehoNaradi : NaradiNaObjednavceReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("0001", "847-8715", "prum. 18", "kotouc");
            SendDefinovanoNaradi("0002", "848-8715", "prum. 18", "kotouc");
            SendDefinovanDodavatel("D005", "Opravar, s.r.o.");
            SendNaOpravu("0001", "D005", "3849/2014", 5, new DateTime(2014, 5, 18));
            SendNaOpravuCislovane("0001", "D005", "3849/2014", 3, new DateTime(2014, 5, 18));
            SendZOpravy("0001", "D005", "3849/2014", 2, 3);
            SendZOpravyCislovane("0001", "D005", "3849/2014", 3);
            SendZOpravy("0001", "D005", "3849/2014", 3);
        }
        protected override void When()
        {
            ZiskatObjednavku("D005", "3849/2014");
        }
        [TestMethod]
        public void NaradiZmiziZeSeznamu()
        {
            Assert.AreEqual(0, _response.PocetCelkem);
            Assert.AreEqual(0, _response.Seznam.Count);
        }
    }

    public class NaradiNaObjednavceReadModelTestBase : ReadModelTestBase
    {
        protected ZiskatNaradiNaObjednavceResponse _response;

        protected void ZiskatObjednavku(string dodavatel, string objednavka)
        {
            _response = ReadProjection<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>(
                new ZiskatNaradiNaObjednavceRequest { KodDodavatele = dodavatel, Objednavka = objednavka });
        }

        protected Guid NaradiId(string zaklad)
        {
            return new Guid("0000" + zaklad + "-0000-0000-0000-0000aaaabecd");
        }

        protected void SendDefinovanoNaradi(string naradiId, string vykres, string rozmer, string druh)
        {
            SendEvent(new DefinovanoNaradiEvent
            {
                NaradiId = NaradiId(naradiId),
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh,
                Verze = 1
            });
        }

        protected void SendDefinovanDodavatel(string kod, string nazev)
        {
            SendEvent(new DefinovanDodavatelEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovan = false,
                Ico = kod,
                Dic = kod,
                Adresa = new[] { nazev, "38001 Dacice" }
            });
        }

        protected void SendNaOpravu(string naradi, string dodavatel, string objednavka, int pocetNovy, DateTime termin, int pocetCelkovy = -1)
        {
            pocetCelkovy = (pocetCelkovy < 0) ? pocetNovy : pocetCelkovy;
            SendEvent(new NecislovaneNaradiPredanoKOpraveEvent
            {
                CelkovaCenaNova = 4m * pocetNovy,
                CelkovaCenaPredchozi = 0m,
                CenaNova = 4m,
                Datum = CurrentTime,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = NaradiId(naradi),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocetNovy, Cena = 4m, Cerstvost = "Pouzite", Datum = CurrentTime } },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocetNovy, Cena = 0m, Cerstvost = "Pouzite", Datum = CurrentTime.AddDays(-1) } },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                Objednavka = objednavka,
                Pocet = pocetNovy,
                PocetNaNovem = pocetCelkovy,
                PocetNaPredchozim = 0,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" },
                TerminDodani = termin,
                TypOpravy = TypOpravy.Oprava,
                Verze = 7
            });
        }
        protected void SendNaOpravuCislovane(string naradi, string dodavatel, string objednavka, int cisloNaradi, DateTime termin)
        {
            SendEvent(new CislovaneNaradiPredanoKOpraveEvent
            {
                CenaNova = 4m,
                Datum = CurrentTime,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = NaradiId(naradi),
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                Objednavka = objednavka,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" },
                TerminDodani = termin,
                TypOpravy = TypOpravy.Oprava,
                Verze = 7,
                CenaPredchozi = 0m,
                CisloNaradi = cisloNaradi
            });
        }

        protected void SendZOpravy(string naradi, string dodavatel, string objednavka, int pocetOpraveny, int pocetZbyvajici = 0)
        {
            SendEvent(new NecislovaneNaradiPrijatoZOpravyEvent
            {
                CelkovaCenaNova = 10m * pocetOpraveny,
                CenaNova = 10m,
                CelkovaCenaPredchozi = 4m * pocetOpraveny,
                Datum = CurrentTime,
                DodaciList = "D" + objednavka,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = NaradiId(naradi),
                Objednavka = objednavka,
                Opraveno = StavNaradiPoOprave.Opraveno,
                Pocet = pocetOpraveny,
                Verze = 9,
                StavNaradi = StavNaradi.VPoradku,
                TypOpravy = TypOpravy.Oprava,
                PocetNaNovem = pocetOpraveny,
                PocetNaPredchozim = pocetZbyvajici,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocetOpraveny, Cena = 4m, Cerstvost = "Opravene", Datum = CurrentTime } },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocetOpraveny, Cena = 0m, Cerstvost = "Pouzite", Datum = CurrentTime.AddDays(-1) } },
            });
        }

        protected void SendZOpravyCislovane(string naradi, string dodavatel, string objednavka, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiPrijatoZOpravyEvent
            {
                CenaNova = 10m,
                Datum = CurrentTime,
                DodaciList = "D" + objednavka,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = NaradiId(naradi),
                Objednavka = objednavka,
                Opraveno = StavNaradiPoOprave.Opraveno,
                CisloNaradi = cisloNaradi,
                Verze = 9,
                CenaPredchozi = 0m,
                StavNaradi = StavNaradi.VPoradku,
                TypOpravy = TypOpravy.Oprava,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
            });
        }

        protected override IEventProjection CreateProjection()
        {
            return new NaradiNaObjednavceProjection(new NaradiNaObjednavceRepository(_folder), _time);
        }

        protected override object CreateReader()
        {
            return new NaradiNaObjednavceReader(new NaradiNaObjednavceRepository(_folder), _time);
        }
    }
}
