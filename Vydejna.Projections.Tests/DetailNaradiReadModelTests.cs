using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Projections.DetailNaradiReadModel;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class DetailNaradiReadModelTest_NeexistujiciNaradi : DetailNaradiReadModelTestBase
    {
        protected override void When()
        {
            ZiskatNaradi();
        }

        [TestMethod]
        public void NaradiIdPodleRequestu()
        {
            Assert.AreEqual(_build.NaradiId, _response.NaradiId);
        }

        [TestMethod]
        public void InformaceONaradiJsouPrazdneRetezce()
        {
            Assert.AreEqual("", _response.Vykres);
            Assert.AreEqual("", _response.Rozmer);
            Assert.AreEqual("", _response.Druh);
            Assert.AreEqual(false, _response.Aktivni);
        }

        [TestMethod]
        public void PoctyNaradiNulove()
        {
            Assert.AreEqual(0, _response.NaSklade);
            AssertPocty(_response.PoctyCelkem, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyCislovane, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyNecislovane, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PrazdneSeznamyCislovanychANecislovanychKusu()
        {
            Assert.IsNotNull(_response.Necislovane);
            Assert.AreEqual(0, _response.Necislovane.Count);
            Assert.IsNotNull(_response.Cislovane);
            Assert.AreEqual(0, _response.Cislovane.Count);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_DefinovaneNaradi : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            _build.Definovano("4839-3341-0", "55x22", "kotouc");
        }

        protected override void When()
        {
            ZiskatNaradi();
        }

        [TestMethod]
        public void InformaceONaradiPrevzatyZDefinice()
        {
            Assert.AreEqual(_build.NaradiId, _response.NaradiId);
            Assert.AreEqual("4839-3341-0", _response.Vykres);
            Assert.AreEqual("55x22", _response.Rozmer);
            Assert.AreEqual("kotouc", _response.Druh);
            Assert.AreEqual(true, _response.Aktivni);
        }

        [TestMethod]
        public void PoctyNaradiNulove()
        {
            Assert.AreEqual(0, _response.NaSklade);
            AssertPocty(_response.PoctyCelkem, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyCislovane, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyNecislovane, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PrazdneSeznamyCislovanychANecislovanychKusu()
        {
            Assert.IsNotNull(_response.Necislovane);
            Assert.AreEqual(0, _response.Necislovane.Count);
            Assert.IsNotNull(_response.Cislovane);
            Assert.AreEqual(0, _response.Cislovane.Count);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_DefiniceZavislostiNepadaji : DetailNaradiReadModelTestBase
    {
        [TestMethod]
        public void DefinovanaVada()
        {
            _master.Vada("9", "bez vady");
            ZiskatNaradi();
        }

        [TestMethod]
        public void DefinovanDodavatel()
        {
            _master.Dodavatel("D005", "EngiTool, a.s.");
            ZiskatNaradi();
        }
        
        [TestMethod]
        public void DefinovanoPracoviste()
        {
            _master.Pracoviste("12345220", "Svarovani", "220");
            ZiskatNaradi();
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_ZmenyNaSklade : DetailNaradiReadModelPresunTestBase
    {
        protected override void Given()
        {
            base.Given();
            _build.ZmenaNaSklade().Zmena(4).Send();
            _build.ZmenaNaSklade().Zmena(3).Send();
        }

        [TestMethod]
        public void NovyPocetNaSklade()
        {
            Assert.AreEqual(7, _response.NaSklade);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_PrijatoNaVydejnu : DetailNaradiReadModelPresunTestBase
    {
        protected override void Given()
        {
            base.Given();
            _build.Prijmout().Necislovane(5).Send();
            _build.Prijmout().Cislovane(3).Send();
            _build.Prijmout().Cislovane(8).Send();
            _build.Prijmout().Necislovane(2).Send();
        }

        [TestMethod]
        public void PocetCelkem()
        {
            AssertPocty(_response.PoctyCelkem, 9, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyNecislovane()
        {
            AssertPocty(_response.PoctyNecislovane, 7, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyCislovane()
        {
            AssertPocty(_response.PoctyCislovane, 2, 0, 0, 0, 0);
        }

        [TestMethod]
        public void SeznamCislovanychObsahujeDveNaradi()
        {
            Assert.AreEqual(2, _response.Cislovane.Count);
        }

        [TestMethod]
        public void CislovaneNaradi_3()
        {
            var cislovane = Cislovane(3);
            Assert.IsNotNull(cislovane);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, cislovane.ZakladUmisteni);
            Assert.IsNotNull(cislovane.NaVydejne);
            Assert.IsNull(cislovane.VeVyrobe);
            Assert.IsNull(cislovane.VOprave);
            Assert.AreEqual("", cislovane.NaVydejne.KodVady);
            Assert.AreEqual("", cislovane.NaVydejne.NazevVady);
            Assert.AreEqual(StavNaradi.VPoradku, cislovane.NaVydejne.StavNaradi);
        }

        [TestMethod]
        public void CislovaneNaradi_8()
        {
            var cislovane = Cislovane(8);
            Assert.IsNotNull(cislovane);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, cislovane.ZakladUmisteni);
            Assert.IsNotNull(cislovane.NaVydejne);
            Assert.IsNull(cislovane.VeVyrobe);
            Assert.IsNull(cislovane.VOprave);
            Assert.AreEqual("", cislovane.NaVydejne.KodVady);
            Assert.AreEqual("", cislovane.NaVydejne.NazevVady);
            Assert.AreEqual(StavNaradi.VPoradku, cislovane.NaVydejne.StavNaradi);
        }

        [TestMethod]
        public void SeznamNecislovanych()
        {
            var necislovane = NecislovaneNaVydejne(StavNaradi.Neurcen);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, necislovane.ZakladUmisteni);
            Assert.IsNotNull(necislovane.NaVydejne);
            Assert.IsNull(necislovane.VeVyrobe);
            Assert.IsNull(necislovane.VOprave);
            Assert.AreEqual(StavNaradi.VPoradku, necislovane.NaVydejne.StavNaradi);
            Assert.AreEqual(7, necislovane.Pocet);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_NeuplneVydanoDoVyroby : DetailNaradiReadModelPresunTestBase
    {
        protected override void Given()
        {
            base.Given();
            _build.Prijmout().Necislovane(10).Send();
            _build.Prijmout().Cislovane(3).Send();
            _build.Vydat().Necislovane(4).Pracoviste("48330330").Send();
            _build.Prijmout().Cislovane(8).Send();
            _build.Vydat().Necislovane(1).Pracoviste("48330330").Send();
            _build.Vydat().Necislovane(1).Pracoviste("84773230").Send();
            _build.Vydat().Cislovane(3).Pracoviste("12345220").Send();
        }

        [TestMethod]
        public void PoctyNecislovane()
        {
            AssertPocty(_response.PoctyNecislovane, 4, 6, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyCislovane()
        {
            AssertPocty(_response.PoctyCislovane, 1, 1, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyCelkove()
        {
            AssertPocty(_response.PoctyCelkem, 5, 7, 0, 0, 0);
        }

        [TestMethod]
        public void ZbyvajiciNecislovaneNaradiNaVydejne()
        {
            var naradi = NecislovaneNaVydejne(StavNaradi.VPoradku);
            Assert.IsNotNull(naradi);
            Assert.AreEqual(4, naradi.Pocet);
        }

        [TestMethod]
        public void VydaneNecislovaneNaradiVeVyrobeA()
        {
            var naradi = NecislovaneVeVyrobe("48330330");
            Assert.IsNotNull(naradi);
            Assert.AreEqual(5, naradi.Pocet);
        }

        [TestMethod]
        public void VydaneNecislovaneNaradiVeVyrobeB()
        {
            var naradi = NecislovaneVeVyrobe("84773230");
            Assert.IsNotNull(naradi);
            Assert.AreEqual(1, naradi.Pocet);
        }

        [TestMethod]
        public void VydaneCislovaneNaradi()
        {
            var naradi = Cislovane(3);
            Assert.AreEqual(ZakladUmisteni.VeVyrobe, naradi.ZakladUmisteni);
            Assert.IsNotNull(naradi.VeVyrobe);
            Assert.AreEqual("12345220", naradi.VeVyrobe.KodPracoviste);
        }

        [TestMethod]
        public void NevydaneCislovaneZustavaNaVydejne()
        {
            var cislovane = _response.Cislovane.FirstOrDefault(c => c.CisloNaradi == 8);
            Assert.IsNotNull(cislovane);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, cislovane.ZakladUmisteni);
            Assert.IsNotNull(cislovane.NaVydejne);
            Assert.IsNull(cislovane.VeVyrobe);
            Assert.IsNull(cislovane.VOprave);
            Assert.AreEqual("", cislovane.NaVydejne.KodVady);
            Assert.AreEqual("", cislovane.NaVydejne.NazevVady);
            Assert.AreEqual(StavNaradi.VPoradku, cislovane.NaVydejne.StavNaradi);
        }

        [TestMethod]
        public void KopirujiSeDataOPracovisti()
        {
            var naradi = NecislovaneVeVyrobe("48330330");
            Assert.IsNotNull(naradi);
            Assert.AreEqual("48330330", naradi.VeVyrobe.KodPracoviste);
            Assert.AreEqual("Kaleni", naradi.VeVyrobe.NazevPracoviste);
            Assert.AreEqual("330", naradi.VeVyrobe.StrediskoPracoviste);
        }
    }

    /*
     * Vydej do vyroby
     * - upravi se pocty
     * - odstrani se puvodni detail, vytvori se detail ve vyrobe podle pracoviste
     * - pri nulovem poctu na puvodnim umisteni se radek odstrani
     * Prijem poskozeneho
     * - upravi se pocty
     * - detail bude pro opravu podle objednavky vcetne dodavatele
     * - pri nulovem poctu na puvodnim umisteni se radek odstrani
     * Navrat opraveneho naradi na vydejnu
     * - pocty
     * - detail na vydejne
     * - pri nulovem poctu na puvodnim umisteni se radek odstrani
     * Prijem zniceneho naradi
     * - pocty
     * - detail na vydejne
     * - pri nulovem poctu na puvodnim umisteni se radek odstrani
     * Odeslani do srotu
     * - pocty
     * - cislovane naradi se odstranuje ze seznamu
     * - pri nulovem poctu na puvodnim umisteni se radek odstrani
     */

    public class DetailNaradiReadModelPresunTestBase : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            _build.Definovano("4839-3341-0", "55x22", "zvedak");
            _master.Vada("1", "klepe");
            _master.Vada("9", "bez vady");
            _master.Dodavatel("D001", "Opravar, s.r.o.");
            _master.Dodavatel("D005", "EngiTool, a.s.");
            _master.Pracoviste("48330330", "Kaleni", "330");
            _master.Pracoviste("84773230", "Brouseni", "230");
            _master.Pracoviste("12345220", "Svarovani", "220");
        }

        protected override void When()
        {
            ZiskatNaradi();
        }
    }

    public class DetailNaradiReadModelTestBase : ReadModelTestBase
    {
        protected DetailNaradiResponse _response;
        protected NaradiTestEventBuilder _build;
        protected ProjectionTestEventBuilder _master;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _master = new ProjectionTestEventBuilder(this);
            _build = _master.Naradi("A");
        }

        protected void AssertPocty(DetailNaradiPocty pocty, int vporadku, int vevyrobe, int poskozene, int voprave, int znicene)
        {
            Assert.IsNotNull(pocty);
            Assert.AreEqual(vporadku, pocty.VPoradku);
            Assert.AreEqual(vevyrobe, pocty.VeVyrobe);
            Assert.AreEqual(poskozene, pocty.Poskozene);
            Assert.AreEqual(voprave, pocty.VOprave);
            Assert.AreEqual(znicene, pocty.Znicene);
        }

        protected DetailNaradiNecislovane NecislovaneNaVydejne(StavNaradi stavNaradi)
        {
            foreach (var naradi in _response.Necislovane)
            {
                if (naradi.ZakladUmisteni != ZakladUmisteni.NaVydejne)
                    continue;
                Assert.IsNotNull(naradi.NaVydejne);
                if (naradi.NaVydejne.StavNaradi == stavNaradi || stavNaradi == StavNaradi.Neurcen)
                    return naradi;
            }
            return null;
        }

        protected DetailNaradiNecislovane NecislovaneVeVyrobe(string pracoviste)
        {
            foreach (var naradi in _response.Necislovane)
            {
                if (naradi.ZakladUmisteni != ZakladUmisteni.VeVyrobe)
                    continue;
                Assert.IsNotNull(naradi.VeVyrobe);
                if (pracoviste == null || naradi.VeVyrobe.KodPracoviste == pracoviste)
                    return naradi;
            }
            return null;
        }

        protected DetailNaradiNecislovane NecislovaneVOprave(string dodavatel, string objednavka)
        {
            foreach (var naradi in _response.Necislovane)
            {
                if (naradi.ZakladUmisteni != ZakladUmisteni.VOprave)
                    continue;
                Assert.IsNotNull(naradi.VOprave);
                var souhlasiDodavatel = dodavatel == null || dodavatel == naradi.VOprave.KodDodavatele;
                var souhlasiObjednavka = objednavka == null || objednavka == naradi.VOprave.Objednavka;
                if (souhlasiDodavatel && souhlasiObjednavka)
                    return naradi;
            }
            return null;
        }

        protected DetailNaradiCislovane Cislovane(int cisloNaradi)
        {
            return _response.Cislovane.Single(n => n.CisloNaradi == cisloNaradi);
        }

        protected void ZiskatNaradi()
        {
            _response = ReadProjection<DetailNaradiRequest, DetailNaradiResponse>(
                new DetailNaradiRequest { NaradiId = _build.NaradiId });
        }

        protected override IEventProjection CreateProjection()
        {
            return new DetailNaradiProjection(new DetailNaradiRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new DetailNaradiReader(new DetailNaradiRepository(_folder), _executor, _time);
        }
    }
}
