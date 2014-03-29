using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Vydejna.Contracts;
using Vydejna.Projections.PrehledObjednavekReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class PrehledObjednavekReadModelTests : ReadModelTestBase
    {
        private PrehledObjednavekResponse _response;
        private DateTime _datum;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _response = null;
            _datum = new DateTime(2014, 3, 18);
        }

        [TestMethod]
        public void NeexistujiciProjekceVraciPrazdnouOdpoved()
        {
            ZiskatStrankuPodleCisla();
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(0, _response.PocetStranek, "PocetStranek");
            Assert.AreEqual(PrehledObjednavekRazeni.PodleCislaObjednavky, _response.Razeni, "Razeni");
            Assert.IsNotNull(_response.Seznam, "Seznam");
            Assert.AreEqual(0, _response.Seznam.Count, "Seznam.Count");
        }

        [TestMethod]
        public void NovaObjednavkaBereZUdalostiUdajeODodavateliAObjednavce()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5, new DateTime(2014, 4, 1), new DateTime(2014, 4, 20));
            ZiskatStrankuPodleCisla();
            Assert.AreEqual(1, _response.Seznam.Count);
            var info = _response.Seznam.Single();
            Assert.AreEqual("D005", info.KodDodavatele);
            Assert.AreEqual("Opravar s.r.o.", info.NazevDodavatele);
            Assert.AreEqual("483/2014", info.Objednavka);
            Assert.AreEqual(new DateTime(2014, 4, 1), info.DatumObjednani);
            Assert.AreEqual(new DateTime(2014, 4, 20), info.TerminDodani);
        }

        [TestMethod]
        public void NovaObjednavkaZacinaSPoctyZUdalosti()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5);
            ZiskatStrankuPodleCisla();
            Assert.AreEqual(1, _response.Seznam.Count);
            var info = _response.Seznam.Single();
            Assert.AreEqual(5, info.PocetObjednanych);
            Assert.AreEqual(0, info.PocetNeopravitelnych);
            Assert.AreEqual(0, info.PocetOpravenych);
        }

        [TestMethod]
        public void NoveObjednavkyMajiSamostatneRadky()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendDefinovanDodavatel("D008", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5);
            SendPredanoKOprave("D008", "483/2014", 5);
            SendPredanoKOprave("D005", "42/2014", 5);
            SendPredanoKOprave("D005", "229/2014", 5);
            SendPredanoKOprave("D008", "102/2014", 5);
            ZiskatStrankuPodleCisla();
            Assert.AreEqual(5, _response.Seznam.Count);
        }

        [TestMethod]
        public void VydejNaExistujiciObjednavkuZvysujePoctyExistujicihoRadku()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendDefinovanDodavatel("D008", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5);
            SendPredanoKOprave("D008", "483/2014", 5);
            SendPredanoKOprave("D005", "42/2014", 5);
            SendPredanoKOprave("D005", "229/2014", 5);
            SendPredanoKOprave("D008", "102/2014", 5);

            SendPredanoKOprave("D005", "229/2014", 3);

            ZiskatStrankuPodleCisla();
            Assert.AreEqual(5, _response.Seznam.Count);
            var radek = _response.Seznam.Single(r => r.KodDodavatele == "D005" && r.Objednavka == "229/2014");
            Assert.AreEqual(8, radek.PocetObjednanych);
            Assert.AreEqual(0, radek.PocetNeopravitelnych);
            Assert.AreEqual(0, radek.PocetOpravenych);
        }

        [TestMethod]
        public void PrijemZObjednavkyZvysujePoctyOpravenychANeopravitelnych()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "229/2014", 8);
            SendOpraveno("D005", "229/2014", "229d/2014", 3, false);
            SendOpraveno("D005", "229/2014", "229d/2014", 4, true);
            ZiskatStrankuPodleCisla();
            var radek = _response.Seznam.Single(r => r.KodDodavatele == "D005" && r.Objednavka == "229/2014");
            Assert.AreEqual(8, radek.PocetObjednanych);
            Assert.AreEqual(3, radek.PocetNeopravitelnych);
            Assert.AreEqual(4, radek.PocetOpravenych);
        }

        [TestMethod]
        public void SeznamSerazenyPodleCislaObjednavky()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendDefinovanDodavatel("D008", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5);
            SendPredanoKOprave("D008", "483/2014", 5);
            SendPredanoKOprave("D005", "42/2014", 5);
            SendPredanoKOprave("D005", "229/2014", 5);
            SendPredanoKOprave("D008", "102/2014", 5);

            ZiskatStrankuPodleCisla();
            AssertRazeni(0, "D008", "102/2014");
            AssertRazeni(1, "D005", "229/2014");
            AssertRazeni(2, "D005", "42/2014");
            AssertRazeni(3, "D005", "483/2014");
            AssertRazeni(4, "D008", "483/2014");
        }

        [TestMethod]
        public void SeznamSerazenyPodleDataObjednani()
        {
            SendDefinovanDodavatel("D005", "Opravar s.r.o.");
            SendDefinovanDodavatel("D008", "Opravar s.r.o.");
            SendPredanoKOprave("D005", "483/2014", 5, new DateTime(2014, 3, 3));
            SendPredanoKOprave("D008", "483/2014", 5, new DateTime(2014, 3, 2));
            SendPredanoKOprave("D005", "42/2014", 5, new DateTime(2014, 3, 7));
            SendPredanoKOprave("D005", "229/2014", 5, new DateTime(2014, 3, 9));
            SendPredanoKOprave("D008", "102/2014", 5, new DateTime(2014, 3, 1));

            ZiskatStrankuPodleData();
            AssertRazeni(0, "D008", "102/2014");
            AssertRazeni(1, "D008", "483/2014");
            AssertRazeni(2, "D005", "483/2014");
            AssertRazeni(3, "D005", "42/2014");
            AssertRazeni(4, "D005", "229/2014");
        }

        private void AssertRazeni(int index, string dodavatel, string objednavka)
        {
            var radek = _response.Seznam[index];
            Assert.AreEqual(dodavatel, radek.KodDodavatele, "[{0}].KodDodavatele", index);
            Assert.AreEqual(objednavka, radek.Objednavka, "[{0}].Objednavka", index);
        }

        private void SendDefinovanDodavatel(string kod, string nazev)
        {
            SendEvent(new DefinovanDodavatelEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovan = nazev == null,
                Ico = "ICO" + kod,
                Dic = "DIC" + kod,
                Adresa = new[] { nazev, "38001 Dacice" }
            });
        }

        private void SendPredanoKOprave(string dodavatel, string objednavka, int pocet, DateTime datumObjednani = default(DateTime), DateTime datumDodani = default(DateTime))
        {
            if (datumObjednani == default(DateTime))
                datumObjednani = _datum;
            if (datumDodani == default(DateTime))
                datumDodani = datumObjednani.AddDays(30);
            SendEvent(new NecislovaneNaradiPredanoKOpraveEvent
            {
                CelkovaCenaNova = 4m * pocet,
                CelkovaCenaPredchozi = 0m,
                CenaNova = 4m,
                Datum = datumObjednani,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = Guid.NewGuid(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocet, Cena = 4m, Cerstvost = "Pouzite", Datum = datumObjednani } },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocet, Cena = 0m, Cerstvost = "Pouzite", Datum = datumObjednani.AddDays(-1) } },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                Objednavka = objednavka,
                Pocet = pocet,
                PocetNaNovem = pocet,
                PocetNaPredchozim = 0,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" },
                TerminDodani = datumDodani,
                TypOpravy = TypOpravy.Oprava,
                Verze = 7
            });
        }

        private void SendOpraveno(string dodavatel, string objednavka, string dodaciList, int pocet, bool opraveno)
        {
            SendEvent(new NecislovaneNaradiPrijatoZOpravyEvent
            {
                CelkovaCenaNova = opraveno ? 10m * pocet : 0m,
                CenaNova = opraveno ? 10m : 0m,
                CelkovaCenaPredchozi = 4m * pocet,
                Datum = _datum,
                DodaciList = dodaciList,
                EventId = Guid.NewGuid(),
                KodDodavatele = dodavatel,
                NaradiId = Guid.NewGuid(),
                Objednavka = objednavka,
                Opraveno = opraveno ? StavNaradiPoOprave.Opraveno : StavNaradiPoOprave.Neopravitelne,
                Pocet = pocet,
                Verze = 9,
                StavNaradi = opraveno ? StavNaradi.VPoradku : StavNaradi.Neopravitelne,
                TypOpravy = TypOpravy.Oprava,
                PocetNaNovem = pocet,
                PocetNaPredchozim = 0,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = opraveno ? "VPoradku" : "Neopravitelne" },
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocet, Cena = 4m, Cerstvost = "Opravene", Datum = _datum } },
                PouziteKusy = new List<SkupinaNecislovanehoNaradiDto>() { new SkupinaNecislovanehoNaradiDto { Pocet = pocet, Cena = 0m, Cerstvost = "Pouzite", Datum = _datum.AddDays(-1) } },
            });
        }

        private void ZiskatStrankuPodleCisla(int stranka = 1)
        {
            _response = ReadProjection<PrehledObjednavekRequest, PrehledObjednavekResponse>(new PrehledObjednavekRequest
            {
                Stranka = stranka,
                Razeni = PrehledObjednavekRazeni.PodleCislaObjednavky
            });
        }

        private void ZiskatStrankuPodleData(int stranka = 1)
        {
            _response = ReadProjection<PrehledObjednavekRequest, PrehledObjednavekResponse>(new PrehledObjednavekRequest
            {
                Stranka = stranka,
                Razeni = PrehledObjednavekRazeni.PodleDataObjednani
            });
        }

        protected override IEventProjection CreateProjection()
        {
            return new PrehledObjednavekProjection(new PrehledObjednavekRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new PrehledObjednavekReader(new PrehledObjednavekRepository(_folder), _executor, _time);
        }
    }
}
