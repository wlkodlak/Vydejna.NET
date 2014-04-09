using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Projections.IndexObjednavekReadModel;
using ServiceLib;
using ServiceLib.Tests.TestUtils;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class IndexObjednavekReadModelTests_Objednavky : IndexObjednavekReadModelTestBase
    {
        [TestMethod]
        public void NeexistujiciObjednavka()
        {
            var response = NajitObjednavku("38223");
            Assert.AreEqual("38223", response.Objednavka, "Objednavka");
            Assert.AreEqual(false, response.Nalezena, "Nalezena");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(0, response.Kandidati.Count, "Kandidati.Count");
        }

        [TestMethod]
        public void NalezenaObjednavka()
        {
            SendDodavatel("D004", "Opravy, s.r.o.");
            SendDodavatel("D008", "Opravy, s.r.o.");
            var termin = CurrentTime.Date.AddDays(30);
            SendOdeslano("D008", "3813/2014", termin);
            var response = NajitObjednavku("3813/2014");
            Assert.AreEqual("3813/2014", response.Objednavka, "Objednavka");
            Assert.AreEqual(true, response.Nalezena, "Nalezena");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(1, response.Kandidati.Count, "Kandidati.Count");
            Assert.AreEqual("D008", response.Kandidati[0].KodDodavatele, "[0].KodDodavatele");
            Assert.AreEqual("Opravy, s.r.o.", response.Kandidati[0].NazevDodavatele, "[0].NazevDodavatele");
            Assert.AreEqual("3813/2014", response.Kandidati[0].Objednavka, "[0].Objednavka");
            Assert.AreEqual(termin, response.Kandidati[0].TerminDodani, "[0].TerminDodani");
        }

        [TestMethod]
        public void ViceKandidatu()
        {
            SendDodavatel("D004", "Opravy, s.r.o.");
            SendDodavatel("D008", "Opravar, s.r.o.");
            var termin1 = CurrentTime.Date.AddDays(30);
            var termin2 = CurrentTime.Date.AddDays(25);
            SendOdeslano("D004", "3813/2014", termin1);
            SendOdeslano("D008", "3813/2014", termin2, 4);
            var response = NajitObjednavku("3813/2014");
            Assert.AreEqual("3813/2014", response.Objednavka, "Objednavka");
            Assert.AreEqual(true, response.Nalezena, "Nalezena");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(2, response.Kandidati.Count, "Kandidati.Count");
            response.Kandidati.Sort((x, y) => string.CompareOrdinal(x.KodDodavatele, y.KodDodavatele));
            Assert.AreEqual("D004", response.Kandidati[0].KodDodavatele, "[0].KodDodavatele");
            Assert.AreEqual("Opravy, s.r.o.", response.Kandidati[0].NazevDodavatele, "[0].NazevDodavatele");
            Assert.AreEqual("3813/2014", response.Kandidati[0].Objednavka, "[0].Objednavka");
            Assert.AreEqual(termin1, response.Kandidati[0].TerminDodani, "[0].TerminDodani");
            Assert.AreEqual("D008", response.Kandidati[1].KodDodavatele, "[1].KodDodavatele");
            Assert.AreEqual("Opravar, s.r.o.", response.Kandidati[1].NazevDodavatele, "[1].NazevDodavatele");
            Assert.AreEqual("3813/2014", response.Kandidati[1].Objednavka, "[1].Objednavka");
            Assert.AreEqual(termin2, response.Kandidati[1].TerminDodani, "[1].TerminDodani");
        }
    }

    [TestClass]
    public class IndexObjednavekReadModelTests_DodaciListy : IndexObjednavekReadModelTestBase
    {
        [TestMethod]
        public void NeexistujiciDodaciList()
        {
            var response = NajitDodaciList("38223");
            Assert.AreEqual("38223", response.DodaciList, "DodaciList");
            Assert.AreEqual(false, response.Nalezen, "Nalezen");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(0, response.Kandidati.Count, "Kandidati.Count");
        }

        [TestMethod]
        public void NalezenDodaciList()
        {
            SendDodavatel("D004", "Opravy, s.r.o.");
            SendDodavatel("D008", "Opravy, s.r.o.");
            var termin = CurrentTime.Date.AddDays(30);
            SendPrijato("D008", "3813/2014", "3813D/2014");
            var response = NajitDodaciList("3813D/2014");
            Assert.AreEqual("3813D/2014", response.DodaciList, "DodaciList");
            Assert.AreEqual(true, response.Nalezen, "Nalezen");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(1, response.Kandidati.Count, "Kandidati.Count");
            Assert.AreEqual("D008", response.Kandidati[0].KodDodavatele, "[0].KodDodavatele");
            Assert.AreEqual("Opravy, s.r.o.", response.Kandidati[0].NazevDodavatele, "[0].NazevDodavatele");
            Assert.AreEqual("3813D/2014", response.Kandidati[0].DodaciList, "[0].DodaciList");
            Assert.AreEqual("3813/2014", string.Join(", ", response.Kandidati[0].Objednavky), "[0].Objednavky");
        }

        [TestMethod]
        public void ViceKandidatu()
        {
            SendDodavatel("D004", "Opravy, s.r.o.");
            SendDodavatel("D008", "Opravar, s.r.o.");
            var termin1 = CurrentTime.Date.AddDays(30);
            var termin2 = CurrentTime.Date.AddDays(25);
            SendPrijato("D004", "3813/2014", "3813D/2014");
            SendPrijato("D008", "3813/2014", "3813D/2014", 2);
            var response = NajitDodaciList("3813D/2014");
            Assert.AreEqual("3813D/2014", response.DodaciList, "DodaciList");
            Assert.AreEqual(true, response.Nalezen, "Nalezen");
            Assert.IsNotNull(response.Kandidati, "Kandidati");
            Assert.AreEqual(2, response.Kandidati.Count, "Kandidati.Count");
        }
    }

    public class IndexObjednavekReadModelTestBase : ReadModelTestBase
    {
        protected void SendDodavatel(string kod, string nazev)
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

        protected void SendOdeslano(string dodavatel, string objednavka, DateTime termin)
        {
            SendEvent(new NecislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                Pocet = 4,
                NaradiId = Guid.NewGuid(),
                Objednavka = objednavka,
                KodDodavatele = dodavatel,
                TerminDodani = termin,
                CelkovaCenaNova = 0m,
                CelkovaCenaPredchozi = 0m,
                CenaNova = 0m,
                PocetNaNovem = 4,
                PocetNaPredchozim = 0,
                TypOpravy = TypOpravy.Oprava,
                Verze = 5,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnaOprava" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { new SkupinaNecislovanehoNaradiDto { Pocet = 4, Cena = 0m, Cerstvost = "P", Datum = CurrentTime } },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto> { new SkupinaNecislovanehoNaradiDto { Pocet = 4, Cena = 0m, Cerstvost = "P", Datum = CurrentTime } },
            });
        }

        protected void SendOdeslano(string dodavatel, string objednavka, DateTime termin, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = Guid.NewGuid(),
                Objednavka = objednavka,
                KodDodavatele = dodavatel,
                TerminDodani = termin,
                CenaNova = 0m,
                TypOpravy = TypOpravy.Oprava,
                Verze = 5,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnaOprava" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                CenaPredchozi = 0m,
                CisloNaradi = cisloNaradi
            });
        }

        protected void SendPrijato(string dodavatel, string objednavka, string dodaciList)
        {
            SendEvent(new NecislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                Pocet = 4,
                NaradiId = Guid.NewGuid(),
                Objednavka = objednavka,
                KodDodavatele = dodavatel,
                CelkovaCenaNova = 0m,
                CelkovaCenaPredchozi = 0m,
                CenaNova = 0m,
                PocetNaNovem = 4,
                PocetNaPredchozim = 0,
                TypOpravy = TypOpravy.Oprava,
                Verze = 5,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto> { new SkupinaNecislovanehoNaradiDto { Pocet = 4, Cena = 0m, Cerstvost = "P", Datum = CurrentTime } },
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { new SkupinaNecislovanehoNaradiDto { Pocet = 4, Cena = 0m, Cerstvost = "O", Datum = CurrentTime } },
                DodaciList = dodaciList,
                Opraveno = StavNaradiPoOprave.Opraveno,
                StavNaradi = StavNaradi.VPoradku
            });
        }

        protected void SendPrijato(string dodavatel, string objednavka, string dodaciList, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = Guid.NewGuid(),
                Objednavka = objednavka,
                KodDodavatele = dodavatel,
                CenaNova = 0m,
                TypOpravy = TypOpravy.Oprava,
                Verze = 5,
                CisloNaradi = cisloNaradi,
                CenaPredchozi = 0m,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                DodaciList = dodaciList,
                Opraveno = StavNaradiPoOprave.Opraveno,
                StavNaradi = StavNaradi.VPoradku
            });
        }

        protected NajitObjednavkuResponse NajitObjednavku(string objednavka)
        {
            return ReadProjection<NajitObjednavkuRequest, NajitObjednavkuResponse>(new NajitObjednavkuRequest { Objednavka = objednavka });
        }

        protected NajitDodaciListResponse NajitDodaciList(string dodaciList)
        {
            return ReadProjection<NajitDodaciListRequest, NajitDodaciListResponse>(new NajitDodaciListRequest { DodaciList = dodaciList });
        }

        protected override IEventProjection CreateProjection()
        {
            return new IndexObjednavekProjection(new IndexObjednavekRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new IndexObjednavekReader(new IndexObjednavekRepository(_folder), _executor, _time);
        }
    }
}
